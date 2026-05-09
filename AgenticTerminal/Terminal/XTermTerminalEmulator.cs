using System.Collections.Concurrent;
using System.Text;
using Hex1b;
using Hex1b.Input;
using Hex1b.Reflow;

namespace AgenticTerminal.Terminal;

public sealed class XTermTerminalEmulator : ITerminalDisplayState, IDisposable
{
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromMilliseconds(250);

    private readonly object _syncRoot = new();
    private readonly CancellationTokenSource _runCancellationSource = new();
    private readonly EmulatorWorkloadAdapter _workloadAdapter;
    private readonly Hex1bTerminal _terminal;
    private readonly TerminalWidgetHandle _terminalHandle;
    private readonly Task _runTask;

    public XTermTerminalEmulator(int columns, int rows, int scrollback = 1000)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        _workloadAdapter = new EmulatorWorkloadAdapter();
        var builder = new Hex1bTerminalBuilder()
            .WithWorkload(_workloadAdapter)
            .WithTerminalWidget(out var terminalHandle)
            .WithDimensions(columns, rows)
            .WithReflow(XtermReflowStrategy.Instance);

        if (scrollback > 0)
        {
            builder = builder.WithScrollback(scrollback, _ => { });
        }

        _terminal = builder.Build();
        _terminalHandle = terminalHandle;
        _terminalHandle.Resize(columns, rows);
        _terminalHandle.OutputReceived += HandleTerminalOutputReceived;
        _runTask = _terminal.RunAsync(_runCancellationSource.Token);

        if (!SpinWait.SpinUntil(() => _terminalHandle.State == TerminalState.Running, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidOperationException("The Hex1b terminal emulator did not start in time.");
        }
    }

    public int Columns
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminalHandle.Width;
            }
        }
    }

    public bool ApplicationCursorKeys
    {
        get
        {
            lock (_syncRoot)
            {
                return false;
            }
        }
    }

    public int Rows
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminalHandle.Height;
            }
        }
    }

    public int CursorColumn
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminalHandle.CursorX;
            }
        }
    }

    public int CursorRow
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminalHandle.CursorY;
            }
        }
    }

    public void Write(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_syncRoot)
        {
            _workloadAdapter.QueueOutput(chunk, _runCancellationSource.Token).GetAwaiter().GetResult();
        }
    }

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            _terminalHandle.Resize(columns, rows);
        }
    }

    public string GenerateKeyInput(Hex1bKey key, Hex1bModifiers modifiers)
    {
        return key switch
        {
            Hex1bKey.UpArrow => BuildCsiCursorSequence('A', modifiers),
            Hex1bKey.DownArrow => BuildCsiCursorSequence('B', modifiers),
            Hex1bKey.RightArrow => BuildCsiCursorSequence('C', modifiers),
            Hex1bKey.LeftArrow => BuildCsiCursorSequence('D', modifiers),
            Hex1bKey.Home => BuildCsiCursorSequence('H', modifiers),
            Hex1bKey.End => BuildCsiCursorSequence('F', modifiers),
            Hex1bKey.PageUp => BuildCsiTildeSequence(5, modifiers),
            Hex1bKey.PageDown => BuildCsiTildeSequence(6, modifiers),
            Hex1bKey.Insert => BuildCsiTildeSequence(2, modifiers),
            Hex1bKey.Delete => BuildCsiTildeSequence(3, modifiers),
            Hex1bKey.Enter => "\r",
            Hex1bKey.Tab when modifiers.HasFlag(Hex1bModifiers.Shift) => "\u001b[Z",
            Hex1bKey.Tab => "\t",
            Hex1bKey.Backspace => "\u007f",
            Hex1bKey.Escape => "\u001b",
            Hex1bKey.Spacebar => GenerateCharInput(' ', modifiers),
            _ => GenerateCharInput(GetPrintableCharacter(key, modifiers), modifiers)
        };
    }

    public string GenerateCharInput(char character, Hex1bModifiers modifiers)
    {
        if (character == '\0')
        {
            return string.Empty;
        }

        var input = character.ToString();
        if (modifiers.HasFlag(Hex1bModifiers.Control))
        {
            input = ConvertControlCharacter(character).ToString();
        }

        if (modifiers.HasFlag(Hex1bModifiers.Alt))
        {
            input = "\u001b" + input;
        }

        return input;
    }

    public IReadOnlyList<string> GetViewportLines()
    {
        lock (_syncRoot)
        {
            var lines = new string[_terminalHandle.Height];
            for (var row = 0; row < _terminalHandle.Height; row++)
            {
                var builder = new StringBuilder(_terminalHandle.Width);
                for (var column = 0; column < _terminalHandle.Width; column++)
                {
                    var cell = _terminalHandle.GetCell(column, row);
                    builder.Append(string.IsNullOrEmpty(cell.Character) ? ' ' : cell.Character);
                }

                lines[row] = builder.ToString();

                if (lines[row].Length < _terminalHandle.Width)
                {
                    lines[row] = lines[row].PadRight(_terminalHandle.Width);
                }
                else if (lines[row].Length > _terminalHandle.Width)
                {
                    lines[row] = lines[row][.._terminalHandle.Width];
                }
            }

            return lines;
        }
    }

    public void Dispose()
    {
        _terminalHandle.OutputReceived -= HandleTerminalOutputReceived;
        _runCancellationSource.Cancel();
        _workloadAdapter.DisposeAsync().AsTask().GetAwaiter().GetResult();

        try
        {
            _runTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _terminal.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _runCancellationSource.Dispose();
    }

    private void HandleTerminalOutputReceived()
    {
        _workloadAdapter.SignalOutputProcessed();
    }

    private static string BuildCsiCursorSequence(char final, Hex1bModifiers modifiers)
    {
        var modifierCode = GetModifierCode(modifiers);
        return modifierCode == 1
            ? $"\u001b[{final}"
            : $"\u001b[1;{modifierCode}{final}";
    }

    private static string BuildCsiTildeSequence(int parameter, Hex1bModifiers modifiers)
    {
        var modifierCode = GetModifierCode(modifiers);
        return modifierCode == 1
            ? $"\u001b[{parameter}~"
            : $"\u001b[{parameter};{modifierCode}~";
    }

    private static int GetModifierCode(Hex1bModifiers modifiers)
    {
        var code = 1;

        if (modifiers.HasFlag(Hex1bModifiers.Shift))
        {
            code += 1;
        }

        if (modifiers.HasFlag(Hex1bModifiers.Alt))
        {
            code += 2;
        }

        if (modifiers.HasFlag(Hex1bModifiers.Control))
        {
            code += 4;
        }

        return code;
    }

    private static char GetPrintableCharacter(Hex1bKey key, Hex1bModifiers modifiers)
    {
        var shifted = modifiers.HasFlag(Hex1bModifiers.Shift);

        if (key is >= Hex1bKey.A and <= Hex1bKey.Z)
        {
            var offset = key - Hex1bKey.A;
            var character = (char)('a' + offset);
            return shifted ? char.ToUpperInvariant(character) : character;
        }

        if (key is >= Hex1bKey.D0 and <= Hex1bKey.D9)
        {
            return (char)('0' + (key - Hex1bKey.D0));
        }

        if (key is >= Hex1bKey.NumPad0 and <= Hex1bKey.NumPad9)
        {
            return (char)('0' + (key - Hex1bKey.NumPad0));
        }

        return key switch
        {
            Hex1bKey.OemComma => shifted ? '<' : ',',
            Hex1bKey.OemPeriod => shifted ? '>' : '.',
            Hex1bKey.OemMinus => shifted ? '_' : '-',
            Hex1bKey.OemPlus => shifted ? '+' : '=',
            Hex1bKey.OemQuestion => shifted ? '?' : '/',
            Hex1bKey.Oem1 => shifted ? ':' : ';',
            Hex1bKey.Oem4 => shifted ? '{' : '[',
            Hex1bKey.Oem5 => shifted ? '|' : '\\',
            Hex1bKey.Oem6 => shifted ? '}' : ']',
            Hex1bKey.Oem7 => shifted ? '"' : '\'',
            Hex1bKey.OemTilde => shifted ? '~' : '`',
            Hex1bKey.Add => '+',
            Hex1bKey.Subtract => '-',
            Hex1bKey.Multiply => '*',
            Hex1bKey.Divide => '/',
            Hex1bKey.Decimal => '.',
            _ => '\0'
        };
    }

    private static char ConvertControlCharacter(char character)
    {
        var upper = char.ToUpperInvariant(character);
        if (upper is >= '@' and <= '_')
        {
            return (char)(upper & 0x1f);
        }

        return character;
    }

    private sealed class EmulatorWorkloadAdapter : IHex1bTerminalWorkloadAdapter, IAsyncDisposable
    {
        private readonly ConcurrentQueue<QueuedOutput> _outputQueue = new();
        private readonly SemaphoreSlim _outputSignal = new(0);
        private volatile TaskCompletionSource<bool>? _currentWriteCompletion;
        private bool _disposed;

        public event Action? Disconnected;

        public async ValueTask QueueOutput(string chunk, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(chunk) || _disposed)
            {
                return;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _outputQueue.Enqueue(new QueuedOutput(Encoding.UTF8.GetBytes(chunk), completion));
            _outputSignal.Release();

            try
            {
                await completion.Task.WaitAsync(OutputDrainTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
            }
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadOutputAsync(CancellationToken cancellationToken)
        {
            while (!_disposed)
            {
                if (_outputQueue.TryDequeue(out var queuedOutput))
                {
                    _currentWriteCompletion = queuedOutput.Completion;
                    return queuedOutput.Bytes;
                }

                await _outputSignal.WaitAsync(cancellationToken);
            }

            return ReadOnlyMemory<byte>.Empty;
        }

        public ValueTask WriteInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public void SignalOutputProcessed()
        {
            _currentWriteCompletion?.TrySetResult(true);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disconnected?.Invoke();
            _currentWriteCompletion?.TrySetResult(true);

            while (_outputQueue.TryDequeue(out var queuedOutput))
            {
                queuedOutput.Completion.TrySetResult(true);
            }

            _outputSignal.Release();
            _outputSignal.Dispose();
            await ValueTask.CompletedTask;
        }

        private sealed record QueuedOutput(byte[] Bytes, TaskCompletionSource<bool> Completion);
    }
}
