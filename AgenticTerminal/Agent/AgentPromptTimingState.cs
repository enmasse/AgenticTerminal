namespace AgenticTerminal.Agent;

public sealed record AgentPromptTimingState(
    DateTimeOffset StartedAt,
    TimeSpan? PersistDuration,
    TimeSpan? SnapshotDuration,
    TimeSpan? SendDuration,
    TimeSpan? TimeToFirstToken,
    TimeSpan? TotalResponseDuration,
    TimeSpan? ActiveToolDuration,
    string? LastToolName,
    bool IsWaitingForFirstToken,
    bool IsCompleted,
    string? LastError);
