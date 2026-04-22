using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgenticTerminal.Terminal;

public sealed class ConPtyTerminalSession : ITerminalSession
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint HandleFlagInherit = 0x00000001;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly CancellationTokenSource _pumpCancellationSource = new();
    private readonly XTermTerminalEmulator _emulator = new(120, 40);
    private readonly TerminalSessionStartupOptions _startupOptions;
    private FileStream? _inputStream;
    private FileStream? _outputStream;
    private Task? _outputPump;
    private Process? _process;
    private IntPtr _pseudoConsole;
    private TerminalCommandCapture? _activeCommandCapture;
    private int _columns = 120;
    private int _rows = 40;
    private bool _disposed;
    private bool _started;

    public ConPtyTerminalSession(TerminalSessionStartupOptions? startupOptions = null)
    {
        _startupOptions = startupOptions ?? new TerminalSessionStartupOptions();
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

            var securityAttributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                InheritHandle = true
            };

            if (!NativeMethods.CreatePipe(out var inputReadSide, out var inputWriteSide, ref securityAttributes, 0))
            {
                throw CreateWin32Exception("Failed to create the ConPTY input pipe.");
            }

            if (!NativeMethods.CreatePipe(out var outputReadSide, out var outputWriteSide, ref securityAttributes, 0))
            {
                inputReadSide.Dispose();
                inputWriteSide.Dispose();
                throw CreateWin32Exception("Failed to create the ConPTY output pipe.");
            }

            try
            {
                if (!NativeMethods.SetHandleInformation(inputWriteSide, HandleFlagInherit, 0))
                {
                    throw CreateWin32Exception("Failed to configure the ConPTY input writer handle.");
                }

                if (!NativeMethods.SetHandleInformation(outputReadSide, HandleFlagInherit, 0))
                {
                    throw CreateWin32Exception("Failed to configure the ConPTY output reader handle.");
                }

                var createResult = NativeMethods.ConptyCreatePseudoConsole(
                    new Coord((short)_columns, (short)_rows),
                    inputReadSide.DangerousGetHandle(),
                    outputWriteSide.DangerousGetHandle(),
                    0,
                    out _pseudoConsole);

                if (createResult < 0)
                {
                    throw new InvalidOperationException($"ConPTY failed to initialize (HRESULT 0x{createResult:X8}).");
                }

                _process = StartShellProcess(_pseudoConsole);
                _inputStream = new FileStream(inputWriteSide, FileAccess.Write, bufferSize: 4096, isAsync: false);
                _outputStream = new FileStream(outputReadSide, FileAccess.Read, bufferSize: 4096, isAsync: false);
                _outputPump = PumpOutputAsync(_pumpCancellationSource.Token);
                _started = true;
            }
            finally
            {
                inputReadSide.Dispose();
                outputWriteSide.Dispose();
            }
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

        var bytes = _encoding.GetBytes(text);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _inputStream!.WriteAsync(bytes, cancellationToken);
            await _inputStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return SendTextAsync(input + "\r", cancellationToken);
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

        var resizeResult = NativeMethods.ConptyResizePseudoConsole(_pseudoConsole, new Coord((short)columns, (short)rows));
        if (resizeResult < 0)
        {
            throw new InvalidOperationException($"ConPTY resize failed (HRESULT 0x{resizeResult:X8}).");
        }

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

            var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
            var script = string.Join(' ',
                "$__agenticterminal_command = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + encodedCommand + "'));",
                "$__agenticterminal_exit = 0;",
                "try { & ([ScriptBlock]::Create($__agenticterminal_command)); if ($LASTEXITCODE -is [int]) { $__agenticterminal_exit = $LASTEXITCODE } }",
                "catch { $__agenticterminal_exit = 1; Write-Host $_; }",
                "finally { Write-Host ('" + TerminalCommandCapture.CompletionMarkerPrefix + ":" + commandId + ":' + $__agenticterminal_exit) }");

            await SendTextAsync(script + "\r", cancellationToken);
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
                        await SendTextAsync("exit\r");
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

            _inputStream?.Dispose();
            _outputStream?.Dispose();
            _process?.Dispose();

            if (_pseudoConsole != IntPtr.Zero)
            {
                NativeMethods.ConptyClosePseudoConsole(_pseudoConsole);
                _pseudoConsole = IntPtr.Zero;
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
        var byteBuffer = new byte[4096];
        var charBuffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await _outputStream!.ReadAsync(byteBuffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            var charsRead = _decoder.GetChars(byteBuffer, 0, bytesRead, charBuffer, 0, flush: false);
            if (charsRead == 0)
            {
                continue;
            }

            ProcessOutputChunk(new string(charBuffer, 0, charsRead));
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

    private Process StartShellProcess(IntPtr pseudoConsole)
    {
        var startupInfo = new StartupInfoEx();
        startupInfo.StartupInfo.Cb = Marshal.SizeOf<StartupInfoEx>();

        var attributeListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        startupInfo.AttributeList = Marshal.AllocHGlobal(attributeListSize);

        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(startupInfo.AttributeList, 1, 0, ref attributeListSize))
            {
                throw CreateWin32Exception("Failed to initialize the ConPTY process attribute list.");
            }

            var pseudoConsolePointer = pseudoConsole;
            if (!NativeMethods.UpdateProcThreadAttribute(
                    startupInfo.AttributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsolePointer,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw CreateWin32Exception("Failed to attach the pseudo console to the shell process.");
            }

            var commandLine = new StringBuilder($"pwsh.exe {TerminalSessionStartupArguments.Build(_startupOptions)}");
            if (!NativeMethods.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent,
                    IntPtr.Zero,
                    Environment.CurrentDirectory,
                    ref startupInfo,
                    out var processInformation))
            {
                throw CreateWin32Exception("Failed to start pwsh inside ConPTY.");
            }

            try
            {
                return Process.GetProcessById((int)processInformation.ProcessId);
            }
            finally
            {
                NativeMethods.CloseHandle(processInformation.ProcessHandle);
                NativeMethods.CloseHandle(processInformation.ThreadHandle);
            }
        }
        finally
        {
            if (startupInfo.AttributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(startupInfo.AttributeList);
                Marshal.FreeHGlobal(startupInfo.AttributeList);
            }
        }
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }

        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Ptr;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SecurityAttributes pipeAttributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(
            string? applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfoEx startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr attributeList,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("conpty.dll", EntryPoint = "ConptyCreatePseudoConsole")]
        public static extern int ConptyCreatePseudoConsole(Coord size, IntPtr inputHandle, IntPtr outputHandle, uint flags, out IntPtr pseudoConsole);

        [DllImport("conpty.dll", EntryPoint = "ConptyResizePseudoConsole")]
        public static extern int ConptyResizePseudoConsole(IntPtr pseudoConsole, Coord size);

        [DllImport("conpty.dll", EntryPoint = "ConptyClosePseudoConsole")]
        public static extern void ConptyClosePseudoConsole(IntPtr pseudoConsole);
    }
}
