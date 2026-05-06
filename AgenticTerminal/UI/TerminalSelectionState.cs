namespace AgenticTerminal.UI;

public sealed class TerminalSelectionState
{
    public bool IsActive { get; set; }

    public int AnchorX { get; set; }

    public int AnchorY { get; set; }

    public int CurrentX { get; set; }

    public int CurrentY { get; set; }

    public void Clear()
    {
        IsActive = false;
        AnchorX = 0;
        AnchorY = 0;
        CurrentX = 0;
        CurrentY = 0;
    }
}
