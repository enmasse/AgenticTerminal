namespace AgenticTerminal.UI;

public sealed record ApplicationShellOptions(bool ShowDebugPanelByDefault)
{
    public static ApplicationShellOptions Default { get; } = new(false);
}
