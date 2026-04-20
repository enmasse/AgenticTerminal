using XTerm.Buffer;
using XTerm.Options;

namespace AgenticTerminal.Terminal;

public sealed class XTermTerminalEmulator : ITerminalDisplayState, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly XTerm.Terminal _terminal;

    public XTermTerminalEmulator(int columns, int rows, int scrollback = 1000)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        _terminal = new XTerm.Terminal(new TerminalOptions
        {
            Cols = columns,
            Rows = rows,
            Scrollback = scrollback,
            ConvertEol = false,
            TermName = "xterm-256color"
        });
    }

    public int Columns
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminal.Cols;
            }
        }
    }

    public bool ApplicationCursorKeys
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminal.ApplicationCursorKeys;
            }
        }
    }

    public int Rows
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminal.Rows;
            }
        }
    }

    public int CursorColumn
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminal.Buffer.X;
            }
        }
    }

    public int CursorRow
    {
        get
        {
            lock (_syncRoot)
            {
                return _terminal.Buffer.Y;
            }
        }
    }

    public void Write(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_syncRoot)
        {
            _terminal.Write(chunk);
        }
    }

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            _terminal.Resize(columns, rows);
        }
    }

    public string GenerateKeyInput(XTerm.Input.Key key, XTerm.Input.KeyModifiers modifiers)
    {
        lock (_syncRoot)
        {
            return _terminal.GenerateKeyInput(key, modifiers);
        }
    }

    public string GenerateCharInput(char character, XTerm.Input.KeyModifiers modifiers)
    {
        lock (_syncRoot)
        {
            return _terminal.GenerateCharInput(character, modifiers);
        }
    }

    public IReadOnlyList<string> GetViewportLines()
    {
        lock (_syncRoot)
        {
            var buffer = _terminal.Buffer;
            var lines = new string[_terminal.Rows];
            for (var row = 0; row < _terminal.Rows; row++)
            {
                var lineIndex = buffer.YDisp + row;
                if (lineIndex < 0 || lineIndex >= buffer.Lines.Length)
                {
                    lines[row] = new string(' ', _terminal.Cols);
                    continue;
                }

                var line = buffer.Lines[lineIndex];
                lines[row] = line?.TranslateToString(trimRight: false, startCol: 0, endCol: _terminal.Cols)
                    ?? new string(' ', _terminal.Cols);

                if (lines[row].Length < _terminal.Cols)
                {
                    lines[row] = lines[row].PadRight(_terminal.Cols);
                }
                else if (lines[row].Length > _terminal.Cols)
                {
                    lines[row] = lines[row][.._terminal.Cols];
                }
            }

            return lines;
        }
    }

    public void Dispose()
    {
        _terminal.Dispose();
    }
}
