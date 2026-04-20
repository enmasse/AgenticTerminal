using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalSnapshotFormatterEmulatorTests
{
    [Fact]
    public void Format_WithXTermTerminalEmulator_IncludesCursorAndVisibleLines()
    {
        var emulator = new XTermTerminalEmulator(columns: 12, rows: 4);
        emulator.Write("PS> ls\r\none\r\ntwo");

        var snapshot = TerminalSnapshotFormatter.Format(emulator, maxLines: 4, maxCharacters: 200);

        Assert.Equal(
            "Cursor: row 3, column 4\nVisible screen:\n01| PS> ls\n02| one\n03| two",
            snapshot);
    }
}
