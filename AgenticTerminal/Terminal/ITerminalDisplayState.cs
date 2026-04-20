namespace AgenticTerminal.Terminal;

public interface ITerminalDisplayState
{
    int Columns { get; }

    int Rows { get; }

    int CursorColumn { get; }

    int CursorRow { get; }

    IReadOnlyList<string> GetViewportLines();
}
