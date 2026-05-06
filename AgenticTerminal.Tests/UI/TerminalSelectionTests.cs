using AgenticTerminal.UI;
using Hex1b;
using Hex1b.Theming;

namespace AgenticTerminal.Tests.UI;

public sealed class TerminalSelectionTests
{
    [Fact]
    public void ExtractSelectionText_WithSoftWrappedVisualLine_JoinsIntoSingleLogicalLine()
    {
        var buffer = CreateBuffer(
            "hello ",
            "world ");
        MarkSoftWrap(buffer, 0, 5);

        var selection = new TerminalSelectionState
        {
            AnchorX = 0,
            AnchorY = 0,
            CurrentX = 4,
            CurrentY = 1,
            IsActive = true
        };

        var text = TerminalSelectionFormatter.ExtractSelectionText(buffer, 6, 2, selection);

        Assert.Equal("hello world", text);
    }

    [Fact]
    public void ExtractSelectionText_WithMultipleLogicalLines_UsesLineBreaks()
    {
        var buffer = CreateBuffer(
            "first ",
            "second");

        var selection = new TerminalSelectionState
        {
            AnchorX = 0,
            AnchorY = 0,
            CurrentX = 5,
            CurrentY = 1,
            IsActive = true
        };

        var text = TerminalSelectionFormatter.ExtractSelectionText(buffer, 6, 2, selection);

        Assert.Equal("first\nsecond", text);
    }

    [Fact]
    public void BuildOverlay_WithSelection_InvertsSelectedCells()
    {
        var selection = new TerminalSelectionState
        {
            AnchorX = 1,
            AnchorY = 0,
            CurrentX = 3,
            CurrentY = 0,
            IsActive = true
        };

        var overlay = TerminalSelectionFormatter.BuildOverlay(6, 2, selection);

        Assert.Equal(CellAttributes.Reverse, overlay[0, 1].Attributes);
        Assert.Equal(CellAttributes.Reverse, overlay[0, 3].Attributes);
        Assert.True(overlay[1, 1].IsTransparent);
    }

    private static TerminalCell[,] CreateBuffer(params string[] rows)
    {
        var height = rows.Length;
        var width = rows.Max(row => row.Length);
        var buffer = new TerminalCell[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var character = x < rows[y].Length ? rows[y][x].ToString() : " ";
                buffer[y, x] = new TerminalCell(character, Hex1bColor.Default, Hex1bColor.Default, CellAttributes.None, 0, DateTimeOffset.UtcNow, null, null, null, UnderlineStyle.None);
            }
        }

        return buffer;
    }

    private static void MarkSoftWrap(TerminalCell[,] buffer, int row, int column)
    {
        var cell = buffer[row, column];
        buffer[row, column] = new TerminalCell(cell.Character, cell.Foreground, cell.Background, cell.Attributes | CellAttributes.SoftWrap, cell.Sequence, cell.WrittenAt, cell.TrackedSixel, cell.TrackedHyperlink, cell.UnderlineColor, cell.UnderlineStyle);
    }
}
