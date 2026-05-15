namespace AgenticTerminal.UI;

public sealed class Hex1bShellState
{
    public TerminalSelectionState TerminalSelection { get; } = new();

    public int TerminalPaneWidth { get; set; } = 90;

    public bool IsDebugPanelVisible { get; set; }

    public bool IsTerminalFocused { get; set; } = true;
}
