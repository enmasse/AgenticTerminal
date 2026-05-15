namespace AgenticTerminal.UI;

public sealed record ApplicationShellOptions(
    bool ShowDebugPanelByDefault,
    AgentPanelMode InitialAgentPanelMode = AgentPanelMode.Embedded)
{
    public static ApplicationShellOptions Default { get; } = new(false);
}
