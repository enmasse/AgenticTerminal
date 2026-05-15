namespace AgenticTerminal.Terminal;

internal enum AgentCommandBufferStream
{
    StandardOutput,
    StandardError
}

internal sealed record AgentCommandBackchannelSummary(
    string CommandId,
    AgentCommandBackchannelEventType EventType,
    string CommandText,
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan? Duration,
    int? ExitCode);

internal sealed record AgentCommandBufferTail(IReadOnlyList<string> Lines);

internal sealed record AgentCommandRecord(
    string CommandId,
    string SessionId,
    string CommandText,
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan? Duration,
    int? ExitCode,
    string StandardOutputTail,
    string StandardErrorTail,
    bool IsCompleted);
