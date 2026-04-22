using System.Reflection;
using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Agent;

public sealed class CopilotAgentSessionManagerApprovalTests
{
    [Fact]
    public async Task ExecuteTerminalCommandAsync_WithTwoApprovedCommands_RunsBothCommandsInOrder()
    {
        var terminalSession = new RecordingTerminalSession();
        await using var manager = new CopilotAgentSessionManager(
            new ApprovalQueue(),
            new ConversationSessionStore(Path.GetTempPath()),
            terminalSession,
            Environment.CurrentDirectory);

        var executeMethod = typeof(CopilotAgentSessionManager).GetMethod(
            "ExecuteTerminalCommandAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(executeMethod);

        var firstTask = InvokeExecuteTerminalCommandAsync(executeMethod!, manager, "Get-Process");
        var secondTask = InvokeExecuteTerminalCommandAsync(executeMethod!, manager, "Get-Service");

        await WaitForPendingApprovalAsync(manager, "Get-Process");
        Assert.True(await manager.ResolvePendingApprovalAsync(true));

        await WaitForPendingApprovalAsync(manager, "Get-Service");
        Assert.True(await manager.ResolvePendingApprovalAsync(true));

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(["Get-Process", "Get-Service"], terminalSession.ExecutedCommands);
    }

    private static async Task<object?> InvokeExecuteTerminalCommandAsync(MethodInfo executeMethod, CopilotAgentSessionManager manager, string command)
    {
        var task = (Task<object?>?)executeMethod.Invoke(manager, [command]);
        Assert.NotNull(task);
        return await task!;
    }

    private static async Task WaitForPendingApprovalAsync(CopilotAgentSessionManager manager, string commandText)
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!cancellationTokenSource.IsCancellationRequested)
        {
            if (string.Equals(manager.PendingApproval?.CommandText, commandText, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(25, cancellationTokenSource.Token);
        }
    }

    private sealed class RecordingTerminalSession : ITerminalSession
    {
        public List<string> ExecutedCommands { get; } = [];

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
        {
            ExecutedCommands.Add(command);
            return Task.FromResult(new TerminalCommandResult(command, string.Empty, 0));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
