using AgenticTerminal.Agent;
using AgenticTerminal.Terminal;

namespace AgenticTerminal.UI;

public static class ApplicationShellFactory
{
    public static IApplicationShell Create(
        CopilotAgentSessionManager sessionManager,
        ITerminalSession terminalSession,
        ApplicationShellOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(terminalSession);

        var resolvedOptions = options ?? ApplicationShellOptions.Default;
        var agentPanelFactory = new AgentPanelFactory(sessionManager, resolvedOptions.InitialAgentPanelMode);

        return new Hex1bApplicationShell(sessionManager, terminalSession, agentPanelFactory, resolvedOptions);
    }
}
