using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;

namespace AgenticTerminal.Tests.UI;

public sealed class AgentShellTextFormatterTests
{
    [Fact]
    public void BuildStatusText_IncludesPendingApprovalCommand()
    {
        var manager = CreateManager();
        var approvalQueue = GetApprovalQueue(manager);
        _ = approvalQueue.EnqueueShellCommandAsync("git status");

        var statusText = AgentShellTextFormatter.BuildStatusText(manager, Hex1bFocusTarget.Terminal);

        Assert.Contains("Prompt: locked while approval is waiting", statusText);
        Assert.Contains("Approval: waiting for single-key confirmation", statusText);
        Assert.Contains("Approval: focus the approval box and press Y or N", statusText);
        Assert.Contains("Focus: terminal", statusText);
    }

    [Theory]
    [InlineData(Hex1bFocusTarget.Terminal, "Focus: terminal")]
    [InlineData(Hex1bFocusTarget.Approval, "Focus: approval")]
    [InlineData(Hex1bFocusTarget.Prompt, "Focus: prompt")]
    [InlineData(Hex1bFocusTarget.Sessions, "Focus: sessions")]
    public void BuildStatusText_ReflectsFocusTarget(Hex1bFocusTarget focusTarget, string expected)
    {
        var manager = CreateManager();

        var statusText = AgentShellTextFormatter.BuildStatusText(manager, focusTarget);

        Assert.Contains(expected, statusText);
    }

    private static CopilotAgentSessionManager CreateManager()
    {
        return new CopilotAgentSessionManager(
            new ApprovalQueue(),
            new ConversationSessionStore(Path.GetTempPath()),
            new TestTerminalSession(),
            Environment.CurrentDirectory);
    }

    private static ApprovalQueue GetApprovalQueue(CopilotAgentSessionManager manager)
    {
        var field = typeof(CopilotAgentSessionManager)
            .GetField("_approvalQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (ApprovalQueue)field.GetValue(manager)!;
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
}
