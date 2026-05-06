using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class AgentCommandBackchannelSessionTests
{
    [Fact]
    public void ObserveEvent_WithCompletedCommand_PersistsBufferInspectionByCommandId()
    {
        using var session = new AgentCommandBackchannelSession("session-1", completedBufferCapacity: 2);

        session.ObserveEvent(new AgentCommandBackchannelEvent
        {
            SessionId = "session-1",
            CommandId = "cmd-1",
            EventType = AgentCommandBackchannelEventType.Started,
            CommandText = "git status",
            ProcessId = 123,
            StartedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        session.ObserveEvent(new AgentCommandBackchannelEvent
        {
            SessionId = "session-1",
            CommandId = "cmd-1",
            EventType = AgentCommandBackchannelEventType.Completed,
            ProcessId = 123,
            StartedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds(),
            CompletedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExitCode = 0,
            StandardOutputTail = "line 1\nline 2\n",
            StandardErrorTail = "warn\n"
        });

        var stdout = session.ReadBufferTail("cmd-1", AgentCommandBufferStream.StandardOutput, 10);
        var stderr = session.ReadBufferTail("cmd-1", AgentCommandBufferStream.StandardError, 10);

        Assert.Equal(["line 1", "line 2"], stdout.Lines);
        Assert.Equal(["warn"], stderr.Lines);
    }

    [Fact]
    public void ObserveEvent_WhenCompletedRetentionIsFull_EvictsOldestCompletedCommand()
    {
        using var session = new AgentCommandBackchannelSession("session-1", completedBufferCapacity: 1);

        session.ObserveEvent(CreateCompleted("cmd-1", "first"));
        session.ObserveEvent(CreateCompleted("cmd-2", "second"));

        Assert.Null(session.TryGetCommand("cmd-1"));
        Assert.NotNull(session.TryGetCommand("cmd-2"));
    }

    private static AgentCommandBackchannelEvent CreateCompleted(string commandId, string stdoutTail)
    {
        return new AgentCommandBackchannelEvent
        {
            SessionId = "session-1",
            CommandId = commandId,
            EventType = AgentCommandBackchannelEventType.Completed,
            ProcessId = 123,
            StartedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds(),
            CompletedAtUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExitCode = 0,
            StandardOutputTail = stdoutTail + "\n"
        };
    }
}
