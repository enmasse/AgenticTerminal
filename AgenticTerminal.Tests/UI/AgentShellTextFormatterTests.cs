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
    [InlineData(Hex1bFocusTarget.UserInput, "Focus: user input")]
    [InlineData(Hex1bFocusTarget.Prompt, "Focus: prompt")]
    [InlineData(Hex1bFocusTarget.Sessions, "Focus: sessions")]
    public void BuildStatusText_ReflectsFocusTarget(Hex1bFocusTarget focusTarget, string expected)
    {
        var manager = CreateManager();

        var statusText = AgentShellTextFormatter.BuildStatusText(manager, focusTarget);

        Assert.Contains(expected, statusText);
    }

    [Fact]
    public void BuildStatusText_IncludesPromptLatencyMeasurements()
    {
        var manager = CreateManager();
        SetLatestPromptTiming(manager, new AgentPromptTimingState(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(25),
            TimeSpan.FromMilliseconds(35),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromMilliseconds(80),
            "read_terminal_snapshot",
            IsWaitingForFirstToken: false,
            IsCompleted: true,
            LastError: null));

        var statusText = AgentShellTextFormatter.BuildStatusText(manager, Hex1bFocusTarget.Terminal);

        Assert.Contains("Latency: persist 15 ms · snapshot 25 ms · send 35 ms", statusText);
        Assert.Contains("Response: first token 250 ms · total 1.50 s", statusText);
        Assert.Contains("Tool timing: read_terminal_snapshot · 80 ms", statusText);
        Assert.Contains("Latency status: completed", statusText);
    }

    [Fact]
    public void BuildPromptTimingText_UsesDefaultMessageWithoutMeasurements()
    {
        var manager = CreateManager();

        var timingText = AgentShellTextFormatter.BuildPromptTimingText(manager);

        Assert.Equal("Latency: no prompt measurements yet", timingText);
    }

    [Fact]
    public void BuildPromptTimingText_ShowsSnapshotAsNotAvailableWhenNoAutomaticSnapshotWasMeasured()
    {
        var manager = CreateManager();
        SetLatestPromptTiming(manager, new AgentPromptTimingState(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(12),
            SnapshotDuration: null,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(180),
            TimeSpan.FromSeconds(0.9),
            ActiveToolDuration: null,
            LastToolName: null,
            IsWaitingForFirstToken: false,
            IsCompleted: true,
            LastError: null));

        var timingText = AgentShellTextFormatter.BuildPromptTimingText(manager);

        Assert.Contains("Latency: persist 12 ms · snapshot n/a · send 40 ms", timingText);
    }

    [Fact]
    public void BuildDebugPanelText_IncludesTimeoutAndInteractionState()
    {
        var manager = CreateManager();
        SetLatestPromptTiming(manager, new AgentPromptTimingState(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(90),
            "run_terminal_command",
            IsWaitingForFirstToken: false,
            IsCompleted: false,
            LastError: null));

        var approvalQueue = GetApprovalQueue(manager);
        _ = approvalQueue.EnqueueShellCommandAsync("git status");

        var debugText = AgentShellTextFormatter.BuildDebugPanelText(manager);

        Assert.Contains("Debug · First-token timeout: 15.00 s", debugText);
        Assert.Contains("Latency: persist 10 ms · snapshot 20 ms · send 30 ms", debugText);
        Assert.Contains("Tool: none", debugText);
        Assert.Contains("Debug · Interaction: approval pending", debugText);
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

    private static void SetLatestPromptTiming(CopilotAgentSessionManager manager, AgentPromptTimingState timing)
    {
        var property = typeof(CopilotAgentSessionManager)
            .GetProperty(nameof(CopilotAgentSessionManager.LatestPromptTiming), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        property.SetValue(manager, timing);
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
