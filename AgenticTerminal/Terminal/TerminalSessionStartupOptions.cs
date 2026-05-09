namespace AgenticTerminal.Terminal;

public sealed record TerminalSessionStartupOptions(
    TerminalSessionMode Mode = TerminalSessionMode.InteractivePseudoConsole,
    bool SuppressPrompt = false,
    bool LoadUserProfile = true,
    int? InitialColumns = null,
    int? InitialRows = null);
