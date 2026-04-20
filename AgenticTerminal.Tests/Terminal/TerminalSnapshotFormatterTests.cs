using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalSnapshotFormatterTests
{
    [Fact]
    public void Format_IncludesCursorAndVisibleLines()
    {
        var buffer = new TerminalScreenBuffer(columns: 12, rows: 4);
        buffer.ApplyChunk("PS> ls\r\none\r\ntwo");

        var snapshot = TerminalSnapshotFormatter.Format(buffer, maxLines: 4, maxCharacters: 200);

        Assert.Equal(
            "Cursor: row 3, column 4\nVisible screen:\n01| PS> ls\n02| one\n03| two",
            snapshot);
    }

    [Fact]
    public void Format_TruncatesToMostRecentLinesWithinCharacterBudget()
    {
        var buffer = new TerminalScreenBuffer(columns: 14, rows: 6);
        buffer.ApplyChunk("alpha\r\nbeta\r\ngamma\r\ndelta\r\nepsilon");

        var snapshot = TerminalSnapshotFormatter.Format(buffer, maxLines: 2, maxCharacters: 80);

        Assert.Equal(
            "Cursor: row 5, column 8\nVisible screen:\n04| delta\n05| epsilon",
            snapshot);
    }
}
