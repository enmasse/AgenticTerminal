namespace AgenticTerminal.Terminal;

public sealed record TerminalCommandResult(string CommandText, string Output, int ExitCode);
