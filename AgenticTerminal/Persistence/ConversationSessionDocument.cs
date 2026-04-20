namespace AgenticTerminal.Persistence;

public sealed record ConversationSessionDocument(
    string SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConversationMessage> Messages);
