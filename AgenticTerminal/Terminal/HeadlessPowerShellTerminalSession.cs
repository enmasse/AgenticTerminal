using System.Diagnostics;
using System.Text;

namespace AgenticTerminal.Terminal;

public sealed class HeadlessPowerShellTerminalSession : ITerminalSession
{
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly XTermTerminalEmulator _emulator = new(120, 40);
    private readonly TerminalSessionStartupOptions _startupOptions;
    private readonly CancellationTokenSource _pumpCancellationSource = new();
    private Process? _process;
    private Task? _standardOutputPump;
    private Task? _standardErrorPump;
    private TerminalCommandCapture? _activeCommandCapture;
    private bool _disposed;
    private bool _started;

    public HeadlessPowerShellTerminalSession(TerminalSessionStartupOptions? startupOptions = null)
    {
        _startupOptions = startupOptions ?? new TerminalSessionStartupOptions(TerminalSessionMode.HeadlessPipe);
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

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = TerminalSessionStartupArguments.Build(_startupOptions),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException("Failed to start headless pwsh.");
            }

            _standardOutputPump = PumpReaderAsync(_process.StandardOutput, isError: false, _pumpCancellationSource.Token);
            _standardErrorPump = PumpReaderAsync(_process.StandardError, isError: true, _pumpCancellationSource.Token);
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
            await _process!.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return SendTextAsync(input + "\n", cancellationToken);
    }

    public async Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

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

            var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
            var script = string.Join(' ',
                "$__agenticterminal_command = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + encodedCommand + "'));",
                "$__agenticterminal_exit = 0;",
                "try { & ([ScriptBlock]::Create($__agenticterminal_command)); if ($LASTEXITCODE -is [int]) { $__agenticterminal_exit = $LASTEXITCODE } }",
                "catch { $__agenticterminal_exit = 1; Write-Host $_; }",
                "finally { Write-Host '" + TerminalCommandCapture.CompletionMarkerPrefix + ":" + commandId + ":' + $__agenticterminal_exit }");

            await SendTextAsync(script + "\n", cancellationToken);
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
                if (_process is { HasExited: false })
                {
                    try
                    {
                        await SendTextAsync("exit\n");
                        await _process.WaitForExitAsync(_pumpCancellationSource.Token);
                    }
                    catch
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            finally
            {
                _pumpCancellationSource.Cancel();
            }

            if (_standardOutputPump is not null)
            {
                try
                {
                    await _standardOutputPump;
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_standardErrorPump is not null)
            {
                try
                {
                    await _standardErrorPump;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _process?.Dispose();
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

    private async Task PumpReaderAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (charactersRead == 0)
            {
                break;
            }

            ProcessOutputChunk(new string(buffer, 0, charactersRead), isError);
        }
    }

    private void ProcessOutputChunk(string chunk, bool isError)
    {
        TerminalCommandCapture? activeCapture;
        lock (_syncRoot)
        {
            activeCapture = _activeCommandCapture;
        }

        if (activeCapture is null)
        {
            EmitOutput(chunk, isError);
            return;
        }

        var visibleText = activeCapture.AppendChunk(chunk);
        if (!string.IsNullOrEmpty(visibleText))
        {
            EmitOutput(visibleText, isError);
        }
    }

    private void EmitOutput(string text, bool isError)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_syncRoot)
        {
            _emulator.Write(text);
        }

        OutputReceived?.Invoke(new TerminalOutputChunk(text, isError, DateTimeOffset.UtcNow));
    }
}
