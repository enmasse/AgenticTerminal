namespace AgenticTerminal.UI;

public sealed class Hex1bShellState
{
    public int TerminalPaneWidth { get; set; } = 90;

    public string PromptText { get; set; } = string.Empty;

    public int SelectedSessionIndex { get; set; }

    public Hex1bFocusTarget FocusTarget { get; set; } = Hex1bFocusTarget.Terminal;
}
