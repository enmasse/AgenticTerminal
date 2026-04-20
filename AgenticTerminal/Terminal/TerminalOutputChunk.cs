namespace AgenticTerminal.Terminal;

public sealed record TerminalOutputChunk(string Text, bool IsError, DateTimeOffset Timestamp);
