using System.Text;

namespace AgenticTerminal.Terminal;

public sealed class TerminalCommandCapture
{
    public const string CompletionMarkerPrefix = "__AGENTICTERMINAL_DONE__";
    private const string InternalCommandVariable = "$__agenticterminal_command";

    private readonly string _commandId;
    private readonly string _commandText;
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _visibleBuffer = new();
    private readonly string _marker;

    public TerminalCommandCapture(string commandId, string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        _commandId = commandId;
        _commandText = commandText;
        _marker = $"{CompletionMarkerPrefix}:{_commandId}:";
    }

    public bool IsCompleted { get; private set; }

    public int ExitCode { get; private set; }

    public string Output { get; private set; } = string.Empty;

    public string CommandText => _commandText;

    public TaskCompletionSource<TerminalCommandResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string AppendChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return string.Empty;
        }

        if (IsCompleted)
        {
            return chunk;
        }

        _buffer.Append(chunk);
        var completeText = _buffer.ToString();
        var markerIndex = FindValidMarkerIndex(completeText);
        if (markerIndex < 0)
        {
            var safeLength = Math.Max(0, completeText.Length - _marker.Length);
            if (safeLength == 0)
            {
                return string.Empty;
            }

            var visibleText = SanitizeVisibleText(completeText[..safeLength], flushAll: false);
            _buffer.Remove(0, safeLength);
            Output += visibleText;
            return visibleText;
        }

        var exitCodeStart = markerIndex + _marker.Length;
        var exitCodeEnd = exitCodeStart;
        while (exitCodeEnd < completeText.Length && (char.IsAsciiDigit(completeText[exitCodeEnd]) || completeText[exitCodeEnd] == '-'))
        {
            exitCodeEnd++;
        }

        if (exitCodeEnd == exitCodeStart)
        {
            return string.Empty;
        }

        if (!int.TryParse(completeText.AsSpan(exitCodeStart, exitCodeEnd - exitCodeStart), out var exitCode))
        {
            return string.Empty;
        }

        var visiblePrefix = SanitizeVisibleText(completeText[..markerIndex], flushAll: true);
        Output += visiblePrefix;
        ExitCode = exitCode;
        IsCompleted = true;
        _buffer.Clear();
        Completion.TrySetResult(new TerminalCommandResult(_commandText, Output, ExitCode));

        var remainder = completeText[exitCodeEnd..];
        if (remainder.StartsWith("\r\n", StringComparison.Ordinal))
        {
            remainder = remainder[2..];
        }
        else if (remainder.StartsWith('\n'))
        {
            remainder = remainder[1..];
        }

        return visiblePrefix + remainder;
    }

    public bool TryComplete(string chunk)
    {
        AppendChunk(chunk);
        return IsCompleted;
    }

    private int FindValidMarkerIndex(string text)
    {
        var searchIndex = 0;
        while (true)
        {
            var markerIndex = text.IndexOf(_marker, searchIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return -1;
            }

            var exitCodeStart = markerIndex + _marker.Length;
            var exitCodeEnd = exitCodeStart;
            while (exitCodeEnd < text.Length && (char.IsAsciiDigit(text[exitCodeEnd]) || text[exitCodeEnd] == '-'))
            {
                exitCodeEnd++;
            }

            if (exitCodeEnd > exitCodeStart)
            {
                return markerIndex;
            }

            searchIndex = markerIndex + _marker.Length;
        }
    }

    private string SanitizeVisibleText(string text, bool flushAll)
    {
        if (string.IsNullOrEmpty(text) && !flushAll)
        {
            return string.Empty;
        }

        _visibleBuffer.Append(text);
        var sanitized = new StringBuilder();

        while (true)
        {
            var tokenIndex = _visibleBuffer.ToString().IndexOf(InternalCommandVariable, StringComparison.Ordinal);
            if (tokenIndex >= 0)
            {
                var lineStart = FindLineStart(_visibleBuffer, tokenIndex);
                if (lineStart > 0)
                {
                    sanitized.Append(_visibleBuffer.ToString(0, lineStart));
                    _visibleBuffer.Remove(0, lineStart);
                    tokenIndex -= lineStart;
                }

                var lineBreakIndex = IndexOfLineBreak(_visibleBuffer);
                if (lineBreakIndex < 0 && !flushAll)
                {
                    break;
                }

                if (tokenIndex > 0)
                {
                    sanitized.Append(_visibleBuffer.ToString(0, tokenIndex));
                }

                sanitized.Append(_commandText);

                if (lineBreakIndex < 0)
                {
                    _visibleBuffer.Clear();
                    break;
                }

                var removeLength = lineBreakIndex + 1;
                if (lineBreakIndex + 1 < _visibleBuffer.Length
                    && _visibleBuffer[lineBreakIndex] == '\r'
                    && _visibleBuffer[lineBreakIndex + 1] == '\n')
                {
                    removeLength++;
                }

                sanitized.Append(_visibleBuffer.ToString(lineBreakIndex, removeLength - lineBreakIndex));
                _visibleBuffer.Remove(0, removeLength);
                continue;
            }

            var emitLength = flushAll
                ? _visibleBuffer.Length
                : Math.Max(0, _visibleBuffer.Length - (InternalCommandVariable.Length - 1));

            if (emitLength == 0)
            {
                break;
            }

            sanitized.Append(_visibleBuffer.ToString(0, emitLength));
            _visibleBuffer.Remove(0, emitLength);
            break;
        }

        return sanitized.ToString();
    }

    private static int FindLineStart(StringBuilder buffer, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (buffer[i] == '\n' || buffer[i] == '\r')
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static int IndexOfLineBreak(StringBuilder buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '\n' || buffer[i] == '\r')
            {
                return i;
            }
        }

        return -1;
    }
}
