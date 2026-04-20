using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class XTermTerminalEmulatorTests
{
    [Fact]
    public void Write_PreservesPartialTextWithoutLineBreaks()
    {
        var emulator = new XTermTerminalEmulator(columns: 8, rows: 3);

        emulator.Write("abc");
        emulator.Write("def");

        Assert.Equal(new[] { "abcdef  ", "        ", "        " }, emulator.GetViewportLines());
    }

    [Fact]
    public void Write_HandlesClearScreenAndCursorHomeSequences()
    {
        var emulator = new XTermTerminalEmulator(columns: 6, rows: 3);

        emulator.Write("first");
        emulator.Write("\u001b[2J\u001b[Hdone");

        Assert.Equal(new[] { "done  ", "      ", "      " }, emulator.GetViewportLines());
        Assert.Equal(4, emulator.CursorColumn);
        Assert.Equal(0, emulator.CursorRow);
    }

    [Fact]
    public void Resize_PreservesVisibleContent()
    {
        var emulator = new XTermTerminalEmulator(columns: 8, rows: 3);

        emulator.Write("hello");
        emulator.Resize(columns: 10, rows: 4);

        Assert.Equal(new[] { "hello     ", "          ", "          ", "          " }, emulator.GetViewportLines());
    }
}
