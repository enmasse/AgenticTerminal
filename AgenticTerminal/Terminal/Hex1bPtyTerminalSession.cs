using System.Text;
using AgenticTerminal.Startup;
using Hex1b;

namespace AgenticTerminal.Terminal;

public sealed class Hex1bPtyTerminalSession : ITerminalSession
{
    private static readonly TimeSpan BackchannelDrainDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ResizeDebounceDelay = TimeSpan.FromMilliseconds(150);
    private const int DefaultColumns = 120;
    private const int DefaultRows = 40;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly CancellationTokenSource _pumpCancellationSource = new();
    private readonly Hex1bTerminalEmulator _emulator;
    private readonly TerminalProcessLaunchConfiguration _launchConfiguration;
    private readonly TerminalSessionStartupOptions _startupOptions;
    private Hex1bTerminalChildProcess? _childProcess;
    private Task? _outputPump;
    private TerminalCommandCapture? _activeCommandCapture;
    private int _columns;
    private int _rows;
    private bool _hasExplicitResizeSinceStart;
    private bool _disposed;
    private bool _started;
    private CancellationTokenSource? _pendingResizeCts;

    private static readonly string DiagLogPath = Path.Combine(Path.GetTempPath(), "AgenticTerminal.diag.log");
    private static void DiagLog(string message)
    {
        try { File.AppendAllText(DiagLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n"); } catch { }
    }

    internal static string BuildSubmittedInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text + "\r";
    }

    internal static string BuildWrappedCommandScript(string command, string commandId, string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return string.Join(' ',
            "__agenticterminal_invoke",
            FormatPowerShellStringLiteral(encodedCommand),
            FormatPowerShellStringLiteral(commandId),
            FormatPowerShellStringLiteral(pipeName));
    }

    private static string FormatPowerShellStringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    internal static bool ShouldSkipResize(
        int currentColumns,
        int currentRows,
        int requestedColumns,
        int requestedRows,
        bool hasExplicitResizeSinceStart)
    {
        return hasExplicitResizeSinceStart
            && requestedColumns == currentColumns
            && requestedRows == currentRows;
    }

    public Hex1bPtyTerminalSession(TerminalSessionStartupOptions? startupOptions = null)
        : this(startupOptions ?? new TerminalSessionStartupOptions(), CreateDefaultLaunchConfiguration(startupOptions ?? new TerminalSessionStartupOptions()))
    {
    }

    internal Hex1bPtyTerminalSession(TerminalSessionStartupOptions startupOptions, TerminalProcessLaunchConfiguration launchConfiguration)
    {
        _startupOptions = startupOptions;
        _launchConfiguration = launchConfiguration;
        _columns = startupOptions.InitialColumns is > 0 ? startupOptions.InitialColumns.Value : DefaultColumns;
        _rows = startupOptions.InitialRows is > 0 ? startupOptions.InitialRows.Value : DefaultRows;
        _emulator = new Hex1bTerminalEmulator(_columns, _rows);
    }

    public event Action<TerminalOutputChunk>? OutputReceived;

    public ITerminalDisplayState DisplayState => _emulator;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            _childProcess = new Hex1bTerminalChildProcess(
                _launchConfiguration.FileName,
                _launchConfiguration.Arguments,
                _launchConfiguration.WorkingDirectory,
                _launchConfiguration.Environment is null ? null : new Dictionary<string, string>(_launchConfiguration.Environment),
                _launchConfiguration.InheritEnvironment,
                _columns,
                _rows);

            DiagLog($"StartAsync: launching PTY with ({_columns},{_rows})");

            await _childProcess.StartAsync(cancellationToken);
            _outputPump = PumpOutputAsync(_pumpCancellationSource.Token);
            _started = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await EnsureStartedAsync(cancellationToken);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _childProcess!.WriteInputAsync(_encoding.GetBytes(text), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return SendTextAsync(BuildSubmittedInput(input), cancellationToken);
    }

    public async Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        if (!_started)
        {
            _columns = columns;
            _rows = rows;

            lock (_syncRoot)
            {
                _emulator.Resize(columns, rows);
            }

            DiagLog($"ResizeAsync pre-start: stored ({columns},{rows})");
            return;
        }

        if (ShouldSkipResize(_columns, _rows, columns, rows, _hasExplicitResizeSinceStart))
        {
            DiagLog($"ResizeAsync post-start: skipped ({columns},{rows}) current=({_columns},{_rows}) hasExplicit={_hasExplicitResizeSinceStart}");
            return;
        }

        DiagLog($"ResizeAsync post-start: debouncing ({_columns},{_rows}) -> ({columns},{rows})");
        _pendingResizeCts?.Cancel();
        _pendingResizeCts = new CancellationTokenSource();
        var deferred = _pendingResizeCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ResizeDebounceDelay, deferred);
                DiagLog($"ResizeAsync post-start: applying ({columns},{rows})");
                await _childProcess!.ResizeAsync(columns, rows, CancellationToken.None);
                _hasExplicitResizeSinceStart = true;
                _columns = columns;
                _rows = rows;
                lock (_syncRoot)
                {
                    _emulator.Resize(columns, rows);
                }
            }
            catch (OperationCanceledException)
            {
                DiagLog($"ResizeAsync post-start: debounce cancelled ({columns},{rows})");
            }
        }, CancellationToken.None);
    }

    public async Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);

        lock (_syncRoot)
        {
            return TerminalSnapshotFormatter.Format(_emulator, options ?? new TerminalSnapshotOptions());
        }
    }

    public async Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await EnsureStartedAsync(cancellationToken);

        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            var commandId = Guid.NewGuid().ToString("N");
            var capture = new TerminalCommandCapture(commandId, command);
            await using var backchannel = new TerminalCommandBackchannel();
            lock (_syncRoot)
            {
                _activeCommandCapture = capture;
            }

            var script = BuildWrappedCommandScript(command, commandId, backchannel.PipeName);

            await SendTextAsync(BuildSubmittedInput(script), cancellationToken);

            var completionMessage = await backchannel.ReadMessageAsync(cancellationToken);
            if (!string.Equals(completionMessage.CommandId, commandId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The terminal command backchannel reported an unexpected command identifier.");
            }

            if (BackchannelDrainDelay > TimeSpan.Zero)
            {
                await Task.Delay(BackchannelDrainDelay, cancellationToken);
            }

            capture.TryCompleteFromBackchannel(completionMessage.ExitCode);
            return await capture.Completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_syncRoot)
            {
                _activeCommandCapture = null;
            }

            _commandLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (!_started)
            {
                return;
            }

            try
            {
                if (_childProcess is { HasExited: false })
                {
                    try
                    {
                        await SendTextAsync(BuildSubmittedInput("exit"));
                        await _childProcess.WaitForExitAsync(_pumpCancellationSource.Token);
                    }
                    catch
                    {
                        _childProcess.Kill();
                    }
                }
            }
            finally
            {
                _pumpCancellationSource.Cancel();
            }

            if (_outputPump is not null)
            {
                try
                {
                    await _outputPump;
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_childProcess is not null)
            {
                await _childProcess.DisposeAsync();
            }

            _started = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        _commandLock.Dispose();
        _lifecycleLock.Dispose();
        _writeLock.Dispose();
        _pumpCancellationSource.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await StartAsync(cancellationToken);
    }

    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var chunk = await _childProcess!.ReadOutputAsync(cancellationToken);
            if (chunk.IsEmpty)
            {
                break;
            }

            var bytes = chunk.ToArray();
            var charCount = _decoder.GetCharCount(bytes, 0, bytes.Length, flush: false);
            if (charCount == 0)
            {
                continue;
            }

            var chars = new char[charCount];
            var charsRead = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
            if (charsRead == 0)
            {
                continue;
            }

            ProcessOutputChunk(new string(chars, 0, charsRead));
        }
    }

    private void ProcessOutputChunk(string chunk)
    {
        TerminalCommandCapture? activeCapture;
        lock (_syncRoot)
        {
            activeCapture = _activeCommandCapture;
        }

        if (activeCapture is null)
        {
            EmitOutput(chunk);
            return;
        }

        var visibleText = activeCapture.AppendChunk(chunk);
        if (!string.IsNullOrEmpty(visibleText))
        {
            EmitOutput(visibleText);
        }
    }

    private void EmitOutput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_syncRoot)
        {
            _emulator.Write(text);
        }

        OutputReceived?.Invoke(new TerminalOutputChunk(text, false, DateTimeOffset.UtcNow));
    }

    private static TerminalProcessLaunchConfiguration CreateDefaultLaunchConfiguration(TerminalSessionStartupOptions startupOptions)
    {
        return new TerminalProcessLaunchConfiguration(
            "pwsh.exe",
            TerminalSessionStartupArguments.BuildArgumentList(startupOptions),
            Environment.CurrentDirectory,
            AgtExecutableBootstrapper.CreateEnvironmentOverrides(),
            true);
    }
}
