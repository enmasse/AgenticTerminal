using System.Text;

namespace AgenticTerminal.Terminal;

public static class TerminalSnapshotFormatter
{
    public static string Format(ITerminalDisplayState buffer, int maxLines, int maxCharacters)
    {
        return Format(buffer, new TerminalSnapshotOptions(maxLines, maxCharacters));
    }

    public static string Format(ITerminalDisplayState buffer, TerminalSnapshotOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(options);

        var viewportLines = buffer.GetViewportLines();
        var endRow = FindSnapshotEndRow(viewportLines, buffer.CursorRow);
        var startRow = Math.Max(0, endRow - Math.Max(1, options.MaxLines) + 1);

        while (startRow < endRow)
        {
            var candidate = BuildSnapshot(viewportLines, startRow, endRow, buffer.CursorRow, buffer.CursorColumn);
            if (candidate.Length <= options.MaxCharacters)
            {
                return candidate;
            }

            startRow++;
        }

        var snapshot = BuildSnapshot(viewportLines, endRow, endRow, buffer.CursorRow, buffer.CursorColumn);
        if (snapshot.Length <= options.MaxCharacters)
        {
            return snapshot;
        }

        return snapshot[..options.MaxCharacters];
    }

    private static int FindSnapshotEndRow(IReadOnlyList<string> viewportLines, int cursorRow)
    {
        for (var row = viewportLines.Count - 1; row >= 0; row--)
        {
            if (!string.IsNullOrWhiteSpace(viewportLines[row]))
            {
                return Math.Max(row, cursorRow);
            }
        }

        return cursorRow;
    }

    private static string BuildSnapshot(IReadOnlyList<string> viewportLines, int startRow, int endRow, int cursorRow, int cursorColumn)
    {
        var builder = new StringBuilder();
        builder.Append("Cursor: row ");
        builder.Append(cursorRow + 1);
        builder.Append(", column ");
        builder.Append(cursorColumn + 1);
        builder.Append('\n');
        builder.Append("Visible screen:\n");

        var width = Math.Max(2, (endRow + 1).ToString().Length);
        for (var row = startRow; row <= endRow; row++)
        {
            builder.Append((row + 1).ToString($"D{width}"));
            builder.Append("| ");
            builder.Append(viewportLines[row].TrimEnd());
            if (row < endRow)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}
