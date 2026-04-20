using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalScreenBufferTests
{
    [Fact]
    public void ApplyChunk_PreservesPartialTextWithoutLineBreaks()
    {
        var buffer = new TerminalScreenBuffer(columns: 8, rows: 3);

        buffer.ApplyChunk("abc");
        buffer.ApplyChunk("def");

        Assert.Equal(new[] { "abcdef  ", "        ", "        " }, buffer.GetViewportLines());
    }

    [Fact]
    public void ApplyChunk_CarriageReturnOverwritesCurrentRow()
    {
        var buffer = new TerminalScreenBuffer(columns: 6, rows: 2);

        buffer.ApplyChunk("hello");
        buffer.ApplyChunk("\rbye");

        Assert.Equal(new[] { "byelo ", "      " }, buffer.GetViewportLines());
    }

    [Fact]
    public void ApplyChunk_HandlesClearScreenAndCursorHomeSequences()
    {
        var buffer = new TerminalScreenBuffer(columns: 6, rows: 3);

        buffer.ApplyChunk("first");
        buffer.ApplyChunk("\u001b[2J\u001b[Hdone");

        Assert.Equal(new[] { "done  ", "      ", "      " }, buffer.GetViewportLines());
    }
}
