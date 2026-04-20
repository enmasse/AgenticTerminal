using System.Collections.Concurrent;
using System.Text;
using Hex1b;

namespace AgenticTerminal.Terminal;

public sealed class Hex1bTerminalSessionWorkloadAdapter : IHex1bTerminalWorkloadAdapter, IAsyncDisposable
{
    private readonly ITerminalSession _terminalSession;
    private readonly ConcurrentQueue<byte[]> _outputQueue = new();
    private readonly SemaphoreSlim _outputSignal = new(0);
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private bool _disposed;

    public Hex1bTerminalSessionWorkloadAdapter(ITerminalSession terminalSession)
    {
        _terminalSession = terminalSession;
        _terminalSession.OutputReceived += HandleOutputReceived;
    }

    public event Action? Disconnected;

    public async ValueTask<ReadOnlyMemory<byte>> ReadOutputAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_outputQueue.TryDequeue(out var chunk))
            {
                return chunk;
            }

            await _outputSignal.WaitAsync(cancellationToken);
        }
    }

    public ValueTask WriteInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        var text = _encoding.GetString(input.Span);
        return new ValueTask(_terminalSession.SendTextAsync(text, cancellationToken));
    }

    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        return new ValueTask(_terminalSession.ResizeAsync(columns, rows, cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _terminalSession.OutputReceived -= HandleOutputReceived;
        Disconnected?.Invoke();
        _outputSignal.Dispose();
        await ValueTask.CompletedTask;
    }

    private void HandleOutputReceived(TerminalOutputChunk outputChunk)
    {
        if (string.IsNullOrEmpty(outputChunk.Text))
        {
            return;
        }

        _outputQueue.Enqueue(_encoding.GetBytes(outputChunk.Text));
        _outputSignal.Release();
    }
}
