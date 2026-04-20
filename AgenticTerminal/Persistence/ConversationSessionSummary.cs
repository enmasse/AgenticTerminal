namespace AgenticTerminal.Persistence;

public sealed record ConversationSessionSummary(
    string SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string LastMessagePreview);
