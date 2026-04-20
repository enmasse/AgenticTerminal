using System.Text;

namespace AgenticTerminal.Terminal;

public sealed class TerminalScreenBuffer : ITerminalDisplayState
{
    private char[][] _cells;
    private int _columns;
    private int _rows;
    private int _cursorColumn;
    private int _cursorRow;

    public TerminalScreenBuffer(int columns, int rows)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        _columns = columns;
        _rows = rows;
        _cells = CreateCells(columns, rows);
    }

    public int CursorColumn => _cursorColumn;

    public int CursorRow => _cursorRow;

    public int Columns => _columns;

    public int Rows => _rows;

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0 || (columns == _columns && rows == _rows))
        {
            return;
        }

        var resized = CreateCells(columns, rows);
        var rowsToCopy = Math.Min(rows, _rows);
        var columnsToCopy = Math.Min(columns, _columns);
        for (var row = 0; row < rowsToCopy; row++)
        {
            Array.Copy(_cells[row], resized[row], columnsToCopy);
        }

        _cells = resized;
        _columns = columns;
        _rows = rows;
        _cursorColumn = Math.Min(_cursorColumn, _columns - 1);
        _cursorRow = Math.Min(_cursorRow, _rows - 1);
    }

    public void ApplyChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        for (var index = 0; index < chunk.Length; index++)
        {
            var current = chunk[index];
            if (current == '\u001b' && index + 1 < chunk.Length && chunk[index + 1] == '[')
            {
                var sequenceEnd = FindCsiSequenceEnd(chunk, index + 2);
                if (sequenceEnd > index)
                {
                    ApplyCsi(chunk[(index + 2)..(sequenceEnd + 1)]);
                    index = sequenceEnd;
                    continue;
                }
            }

            switch (current)
            {
                case '\r':
                    _cursorColumn = 0;
                    break;
                case '\n':
                    MoveToNextRow();
                    break;
                case '\b':
                    _cursorColumn = Math.Max(0, _cursorColumn - 1);
                    break;
                case '\t':
                    var spaces = 4 - (_cursorColumn % 4);
                    for (var i = 0; i < spaces; i++)
                    {
                        WriteCharacter(' ');
                    }
                    break;
                default:
                    if (!char.IsControl(current))
                    {
                        WriteCharacter(current);
                    }
                    break;
            }
        }
    }

    public IReadOnlyList<string> GetViewportLines()
    {
        var result = new string[_rows];
        for (var row = 0; row < _rows; row++)
        {
            result[row] = new string(_cells[row]);
        }

        return result;
    }

    private static char[][] CreateCells(int columns, int rows)
    {
        var cells = new char[rows][];
        for (var row = 0; row < rows; row++)
        {
            cells[row] = Enumerable.Repeat(' ', columns).ToArray();
        }

        return cells;
    }

    private void WriteCharacter(char character)
    {
        _cells[_cursorRow][_cursorColumn] = character;
        _cursorColumn++;
        if (_cursorColumn >= _columns)
        {
            _cursorColumn = 0;
            MoveToNextRow();
        }
    }

    private void MoveToNextRow()
    {
        _cursorRow++;
        if (_cursorRow < _rows)
        {
            return;
        }

        for (var row = 1; row < _rows; row++)
        {
            Array.Copy(_cells[row], _cells[row - 1], _columns);
        }

        Array.Fill(_cells[_rows - 1], ' ');
        _cursorRow = _rows - 1;
    }

    private int FindCsiSequenceEnd(string chunk, int startIndex)
    {
        for (var index = startIndex; index < chunk.Length; index++)
        {
            var current = chunk[index];
            if (current is >= '@' and <= '~')
            {
                return index;
            }
        }

        return -1;
    }

    private void ApplyCsi(string sequence)
    {
        if (string.IsNullOrEmpty(sequence))
        {
            return;
        }

        var final = sequence[^1];
        var arguments = sequence[..^1];
        var parameters = ParseParameters(arguments);

        switch (final)
        {
            case 'A':
                _cursorRow = Math.Max(0, _cursorRow - GetParameter(parameters, 0, 1));
                break;
            case 'B':
                _cursorRow = Math.Min(_rows - 1, _cursorRow + GetParameter(parameters, 0, 1));
                break;
            case 'C':
                _cursorColumn = Math.Min(_columns - 1, _cursorColumn + GetParameter(parameters, 0, 1));
                break;
            case 'D':
                _cursorColumn = Math.Max(0, _cursorColumn - GetParameter(parameters, 0, 1));
                break;
            case 'H':
            case 'f':
                _cursorRow = Math.Clamp(GetParameter(parameters, 0, 1) - 1, 0, _rows - 1);
                _cursorColumn = Math.Clamp(GetParameter(parameters, 1, 1) - 1, 0, _columns - 1);
                break;
            case 'J':
                if (GetParameter(parameters, 0, 0) == 2)
                {
                    ClearAll();
                }
                break;
            case 'K':
                ClearLine(GetParameter(parameters, 0, 0));
                break;
            case 'm':
                break;
        }
    }

    private void ClearAll()
    {
        for (var row = 0; row < _rows; row++)
        {
            Array.Fill(_cells[row], ' ');
        }

        _cursorRow = 0;
        _cursorColumn = 0;
    }

    private void ClearLine(int mode)
    {
        switch (mode)
        {
            case 2:
                Array.Fill(_cells[_cursorRow], ' ');
                break;
            case 1:
                for (var column = 0; column <= _cursorColumn; column++)
                {
                    _cells[_cursorRow][column] = ' ';
                }
                break;
            default:
                for (var column = _cursorColumn; column < _columns; column++)
                {
                    _cells[_cursorRow][column] = ' ';
                }
                break;
        }
    }

    private static int[] ParseParameters(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            return [];
        }

        var normalized = arguments.StartsWith('?') ? arguments[1..] : arguments;
        return normalized.Split(';', StringSplitOptions.None)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .ToArray();
    }

    private static int GetParameter(int[] parameters, int index, int defaultValue)
    {
        return parameters.Length > index && parameters[index] != 0
            ? parameters[index]
            : defaultValue;
    }
}
