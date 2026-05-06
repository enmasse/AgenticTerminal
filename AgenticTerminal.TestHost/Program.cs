using System.Text;
using System.IO.Pipes;
using System.Text.RegularExpressions;

Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

await WritePromptAsync();

var buffer = new StringBuilder();
var previousWasCarriageReturn = false;
var input = new char[1];

while (await Console.In.ReadAsync(input.AsMemory(0, 1)) is 1)
{
    var current = input[0];

    if (current == '\r')
    {
        await ProcessLineAsync(buffer.ToString());
        buffer.Clear();
        previousWasCarriageReturn = true;
        continue;
    }

    if (current == '\n')
    {
        if (!previousWasCarriageReturn)
        {
            await ProcessLineAsync(buffer.ToString());
            buffer.Clear();
        }

        previousWasCarriageReturn = false;
        continue;
    }

    previousWasCarriageReturn = false;
    buffer.Append(current);
}

static async Task ProcessLineAsync(string line)
{
    if (string.Equals(line, "exit", StringComparison.Ordinal))
    {
        await Console.Out.WriteLineAsync("RECEIVED:exit");
        await Console.Out.WriteLineAsync("EXIT");
        await Console.Out.FlushAsync();
        Environment.Exit(0);
    }

    if (TryHandleWrappedCommand(line, out var decodedCommand, out var commandId, out var pipeName))
    {
        await Console.Out.WriteLineAsync("RECEIVED:WRAPPED-COMMAND");
        await Console.Out.WriteLineAsync($"COMMAND:{decodedCommand}");
        await Console.Out.WriteLineAsync($"OUTPUT:{decodedCommand.ToUpperInvariant()}");
        await SendCompletionAsync(pipeName, commandId, 0);
    }
    else
    {
        await Console.Out.WriteLineAsync($"RECEIVED:{Escape(line)}");
        await Console.Out.WriteLineAsync($"INPUT:{line}");
    }

    await WritePromptAsync();
}

static async Task WritePromptAsync()
{
    Console.Write("$ ");
    await Console.Out.FlushAsync();
}

static bool TryHandleWrappedCommand(string line, out string decodedCommand, out string commandId, out string pipeName)
{
    decodedCommand = string.Empty;
    commandId = string.Empty;
    pipeName = string.Empty;

    var encodedCommandMatch = Regex.Match(line, @"FromBase64String\('(?<command>[^']+)'\)", RegexOptions.CultureInvariant);
    var commandIdMatch = Regex.Match(line, @"WriteLine\('(?<id>[^']+):' \+ \$__agenticterminal_exit\)", RegexOptions.CultureInvariant);
    var pipeNameMatch = Regex.Match(line, @"NamedPipeClientStream\]::new\('\.', '(?<pipe>[^']+)', \[System\.IO\.Pipes\.PipeDirection\]::Out\)", RegexOptions.CultureInvariant);
    if (!encodedCommandMatch.Success || !commandIdMatch.Success || !pipeNameMatch.Success)
    {
        return false;
    }

    try
    {
        decodedCommand = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCommandMatch.Groups["command"].Value));
        commandId = commandIdMatch.Groups["id"].Value;
        pipeName = pipeNameMatch.Groups["pipe"].Value;
        return true;
    }
    catch (FormatException)
    {
        decodedCommand = string.Empty;
        commandId = string.Empty;
        pipeName = string.Empty;
        return false;
    }
}

static async Task SendCompletionAsync(string pipeName, string commandId, int exitCode)
{
    try
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync($"{commandId}:{exitCode}");
    }
    catch (IOException)
    {
    }
    catch (TimeoutException)
    {
    }
}

static string Escape(string text)
{
    return text
        .Replace("\r", "<CR>", StringComparison.Ordinal)
        .Replace("\n", "<LF>", StringComparison.Ordinal);
}

public sealed class TestHostMarker;
