using Hex1b;
using Hex1b.Surfaces;

namespace AgenticTerminal.UI;

internal static class TerminalSelectionFormatter
{
    public static string ExtractSelectionText(TerminalCell[,] buffer, int width, int height, TerminalSelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(selection);

        if (!selection.IsActive || width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        var bounds = Normalize(selection, width, height);
        var logicalLines = BuildLogicalLines(buffer, width, height);
        var selectedLines = new List<string>();

        foreach (var logicalLine in logicalLines)
        {
            if (logicalLine.EndY < bounds.StartY || logicalLine.StartY > bounds.EndY)
            {
                continue;
            }

            var startIndex = 0;
            var endIndex = logicalLine.Text.Length - 1;

            if (logicalLine.StartY == bounds.StartY)
            {
                startIndex = Math.Clamp(logicalLine.ToLogicalIndex(bounds.StartY, bounds.StartX), 0, logicalLine.Text.Length);
            }

            if (logicalLine.EndY == bounds.EndY)
            {
                endIndex = Math.Clamp(logicalLine.ToLogicalIndex(bounds.EndY, bounds.EndX), -1, logicalLine.Text.Length - 1);
            }

            if (logicalLine.StartY < bounds.StartY)
            {
                startIndex = 0;
            }

            if (logicalLine.EndY > bounds.EndY)
            {
                endIndex = logicalLine.Text.Length - 1;
            }

            if (logicalLine.Text.Length == 0 || endIndex < startIndex)
            {
                continue;
            }

            selectedLines.Add(logicalLine.Text[startIndex..(endIndex + 1)]);
        }

        return string.Join("\n", selectedLines);
    }

    public static SurfaceCell[,] BuildOverlay(int width, int height, TerminalSelectionState selection)
    {
        var overlay = new SurfaceCell[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                overlay[y, x] = new SurfaceCell(" ", null, null, CellAttributes.None, 1, null, null, null, UnderlineStyle.None);
            }
        }

        if (!selection.IsActive || width <= 0 || height <= 0)
        {
            return overlay;
        }

        var bounds = Normalize(selection, width, height);
        for (var y = bounds.StartY; y <= bounds.EndY; y++)
        {
            var startX = y == bounds.StartY ? bounds.StartX : 0;
            var endX = y == bounds.EndY ? bounds.EndX : width - 1;
            for (var x = startX; x <= endX; x++)
            {
                overlay[y, x] = new SurfaceCell(" ", null, null, CellAttributes.Reverse, 1, null, null, null, UnderlineStyle.None);
            }
        }

        return overlay;
    }

    private static SelectionBounds Normalize(TerminalSelectionState selection, int width, int height)
    {
        var anchorX = Math.Clamp(selection.AnchorX, 0, width - 1);
        var anchorY = Math.Clamp(selection.AnchorY, 0, height - 1);
        var currentX = Math.Clamp(selection.CurrentX, 0, width - 1);
        var currentY = Math.Clamp(selection.CurrentY, 0, height - 1);

        if (anchorY > currentY || (anchorY == currentY && anchorX > currentX))
        {
            (anchorX, currentX) = (currentX, anchorX);
            (anchorY, currentY) = (currentY, anchorY);
        }

        return new SelectionBounds(anchorX, anchorY, currentX, currentY);
    }

    private static List<LogicalLine> BuildLogicalLines(TerminalCell[,] buffer, int width, int height)
    {
        var lines = new List<LogicalLine>();
        var text = new System.Text.StringBuilder();
        var positions = new List<(int X, int Y)>();
        var lineStartY = 0;

        for (var y = 0; y < height; y++)
        {
            if (text.Length == 0)
            {
                lineStartY = y;
            }

            var rowText = new char[width];
            for (var x = 0; x < width; x++)
            {
                var character = buffer[y, x].Character;
                rowText[x] = string.IsNullOrEmpty(character) ? ' ' : character[0];
                positions.Add((x, y));
            }

            var preserveTrailingSpace = y < height - 1 && buffer[y, width - 1].IsSoftWrap;
            var rowString = preserveTrailingSpace
                ? new string(rowText)
                : new string(rowText).TrimEnd();
            text.Append(rowString);
            if (positions.Count > text.Length)
            {
                positions.RemoveRange(text.Length, positions.Count - text.Length);
            }

            var softWrap = width > 0 && buffer[y, Math.Min(width - 1, width - 1)].IsSoftWrap;
            if (softWrap)
            {
                continue;
            }

            lines.Add(new LogicalLine(lineStartY, y, text.ToString(), [.. positions]));
            text.Clear();
            positions.Clear();
        }

        if (text.Length > 0)
        {
            lines.Add(new LogicalLine(lineStartY, height - 1, text.ToString(), [.. positions]));
        }

        return lines;
    }

    private readonly record struct SelectionBounds(int StartX, int StartY, int EndX, int EndY);

    private sealed record LogicalLine(int StartY, int EndY, string Text, IReadOnlyList<(int X, int Y)> Positions)
    {
        public int ToLogicalIndex(int row, int column)
        {
            for (var index = 0; index < Positions.Count; index++)
            {
                var position = Positions[index];
                if (position.Y > row || (position.Y == row && position.X >= column))
                {
                    return index;
                }
            }

            return Positions.Count;
        }
    }
}
