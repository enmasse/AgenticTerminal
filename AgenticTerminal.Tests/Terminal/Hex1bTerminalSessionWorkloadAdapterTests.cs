using System.Text;
using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class Hex1bTerminalSessionWorkloadAdapterTests
{
    [Fact]
    public async Task ReadOutputAsync_ReturnsQueuedTerminalOutput()
    {
        var session = new TestTerminalSession();
        var adapter = new Hex1bTerminalSessionWorkloadAdapter(session);

        session.RaiseOutput("dir\r\n");
        var output = await adapter.ReadOutputAsync(CancellationToken.None);

        Assert.Equal("dir\r\n", Encoding.UTF8.GetString(output.Span));
    }

    [Fact]
    public async Task WriteInputAsync_ForwardsInputToTerminalSession()
    {
        var session = new TestTerminalSession();
        var adapter = new Hex1bTerminalSessionWorkloadAdapter(session);

        await adapter.WriteInputAsync(Encoding.UTF8.GetBytes("pwd").AsMemory(), CancellationToken.None);

        Assert.Equal("pwd", session.LastSentText);
    }

    [Fact]
    public async Task ResizeAsync_ForwardsResizeToTerminalSession()
    {
        var session = new TestTerminalSession();
        var adapter = new Hex1bTerminalSessionWorkloadAdapter(session);

        await adapter.ResizeAsync(120, 40, CancellationToken.None);

        Assert.Equal((120, 40), session.LastResize);
    }

    private sealed class TestTerminalSession : ITerminalSession
    {
        public event Action<TerminalOutputChunk>? OutputReceived;

        public ITerminalDisplayState DisplayState { get; } = new TerminalScreenBuffer(80, 24);

        public string? LastSentText { get; private set; }

        public (int Columns, int Rows)? LastResize { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            LastSentText = text;
            return Task.CompletedTask;
        }

        public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            LastResize = (columns, rows);
            return Task.CompletedTask;
        }

        public Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
            => Task.FromResult(new TerminalCommandResult(command, string.Empty, 0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseOutput(string text)
        {
            OutputReceived?.Invoke(new TerminalOutputChunk(text, false, DateTimeOffset.UtcNow));
        }
    }
}
