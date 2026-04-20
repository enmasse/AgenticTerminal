using AgenticTerminal.Agent;
using AgenticTerminal.Terminal;

namespace AgenticTerminal.UI;

public static class ApplicationShellFactory
{
    public static IApplicationShell Create(CopilotAgentSessionManager sessionManager, ITerminalSession terminalSession)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(terminalSession);

        return new Hex1bApplicationShell(sessionManager, terminalSession);
    }
}
