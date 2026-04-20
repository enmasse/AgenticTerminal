namespace AgenticTerminal.Terminal;

public interface ITerminalSession : IAsyncDisposable
{
    event Action<TerminalOutputChunk>? OutputReceived;

    ITerminalDisplayState DisplayState { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    Task SubmitInputAsync(string input, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);

    Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default);

    Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);
}
