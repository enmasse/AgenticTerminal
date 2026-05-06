using System.IO.Pipes;
using System.Text;

namespace AgenticTerminal.Terminal;

internal sealed class TerminalCommandBackchannel : IAsyncDisposable
{
    private readonly NamedPipeServerStream _server;

    public TerminalCommandBackchannel()
    {
        PipeName = $"agenticterminal-{Guid.NewGuid():N}";
        _server = new NamedPipeServerStream(
            PipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public string PipeName { get; }

    public async Task<TerminalCommandBackchannelMessage> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        await _server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(_server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (!TryParseMessage(line, out var message))
        {
            throw new InvalidOperationException("The terminal command backchannel message was invalid.");
        }

        return message;
    }

    public ValueTask DisposeAsync()
    {
        _server.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static bool TryParseMessage(string? messageText, out TerminalCommandBackchannelMessage message)
    {
        message = default;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var separatorIndex = messageText.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == messageText.Length - 1)
        {
            return false;
        }

        var commandId = messageText[..separatorIndex];
        if (string.IsNullOrWhiteSpace(commandId)
            || !int.TryParse(messageText.AsSpan(separatorIndex + 1), out var exitCode))
        {
            return false;
        }

        message = new TerminalCommandBackchannelMessage(commandId, exitCode);
        return true;
    }
}

internal readonly record struct TerminalCommandBackchannelMessage(string CommandId, int ExitCode);
