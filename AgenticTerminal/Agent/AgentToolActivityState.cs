namespace AgenticTerminal.Agent;

public sealed record AgentToolActivityState(
    string ToolName,
    string? DisplayName,
    string? ArgumentsSummary,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool IsRunning,
    bool Succeeded,
    string? ResultSummary);
