using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;

namespace AgenticTerminal.Tests.UI;

public sealed class ApplicationShellFactoryTests
{
    [Fact]
    public void Create_ReturnsHex1bShellByDefault()
    {
        using var tempDirectory = new TemporaryDirectory();
        var sessionManager = CreateSessionManager(tempDirectory.Path);
        var terminalSession = new TestTerminalSession();

        var shell = ApplicationShellFactory.Create(sessionManager, terminalSession);

        Assert.IsType<Hex1bApplicationShell>(shell);
    }

    private static CopilotAgentSessionManager CreateSessionManager(string appDataPath)
    {
        return new CopilotAgentSessionManager(
            new ApprovalQueue(),
            new ConversationSessionStore(appDataPath),
            new TestTerminalSession(),
            Environment.CurrentDirectory);
    }

    private sealed class TestTerminalSession : ITerminalSession
    {
        event Action<TerminalOutputChunk>? ITerminalSession.OutputReceived
        {
            add { }
            remove { }
        }

        public ITerminalDisplayState DisplayState { get; } = new TerminalScreenBuffer(80, 24);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
            => Task.FromResult(new TerminalCommandResult(command, string.Empty, 0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
