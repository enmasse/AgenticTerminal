namespace AgenticTerminal.Terminal;

internal sealed record TerminalProcessLaunchConfiguration(
    string FileName,
    string[] Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    bool InheritEnvironment);
