namespace AgenticTerminal.Persistence;

public sealed record ConversationMessage(string Role, string Content, DateTimeOffset Timestamp);
