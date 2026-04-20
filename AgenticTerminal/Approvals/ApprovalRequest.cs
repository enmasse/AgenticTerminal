namespace AgenticTerminal.Approvals;

public sealed record ApprovalRequest(string Id, string CommandText, DateTimeOffset RequestedAt);
