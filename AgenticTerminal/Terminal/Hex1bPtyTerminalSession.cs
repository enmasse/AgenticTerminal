using System.Text;
using Hex1b;

namespace AgenticTerminal.Terminal;

public sealed class Hex1bPtyTerminalSession : ITerminalSession
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly CancellationTokenSource _pumpCancellationSource = new();
    private readonly XTermTerminalEmulator _emulator = new(120, 40);
    private readonly TerminalProcessLaunchConfiguration _launchConfiguration;
    private readonly TerminalSessionStartupOptions _startupOptions;
    private Hex1bTerminalChildProcess? _childProcess;
    private Task? _outputPump;
    private TerminalCommandCapture? _activeCommandCapture;
    private int _columns = 120;
    private int _rows = 40;
    private bool _disposed;
    private bool _started;

    internal static string BuildSubmittedInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text + "\r";
    }

    internal static string BuildWrappedCommandScript(string command, string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return string.Join(' ',
            "$__agenticterminal_command = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + encodedCommand + "'));",
            "$__agenticterminal_exit = 0;",
            "try { & ([ScriptBlock]::Create($__agenticterminal_command)); if ($LASTEXITCODE -is [int]) { $__agenticterminal_exit = $LASTEXITCODE } }",
            "catch { $__agenticterminal_exit = 1; Write-Host $_; }",
            "finally { Write-Host ('" + TerminalCommandCapture.CompletionMarkerPrefix + ":" + commandId + ":' + $__agenticterminal_exit) }");
    }

    public Hex1bPtyTerminalSession(TerminalSessionStartupOptions? startupOptions = null)
        : this(startupOptions ?? new TerminalSessionStartupOptions(), CreateDefaultLaunchConfiguration(startupOptions ?? new TerminalSessionStartupOptions()))
    {
    }

    internal Hex1bPtyTerminalSession(TerminalSessionStartupOptions startupOptions, TerminalProcessLaunchConfiguration launchConfiguration)
    {
        _startupOptions = startupOptions;
        _launchConfiguration = launchConfiguration;
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

        await EnsureStartedAsync(cancellationToken);

        if (columns == _columns && rows == _rows)
        {
            return;
        }

        await _childProcess!.ResizeAsync(columns, rows, cancellationToken);
        _columns = columns;
        _rows = rows;

        lock (_syncRoot)
        {
            _emulator.Resize(columns, rows);
        }
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
            lock (_syncRoot)
            {
                _activeCommandCapture = capture;
            }

            var script = BuildWrappedCommandScript(command, commandId);

            await SendTextAsync(BuildSubmittedInput(script), cancellationToken);
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
            null,
            true);
    }
}
