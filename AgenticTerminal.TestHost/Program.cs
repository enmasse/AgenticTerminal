using System.Text;

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

    if (TryHandleWrappedCommand(line, out var decodedCommand, out var completionMarker))
    {
        await Console.Out.WriteLineAsync("RECEIVED:WRAPPED-COMMAND");
        await Console.Out.WriteLineAsync($"COMMAND:{decodedCommand}");
        await Console.Out.WriteLineAsync($"OUTPUT:{decodedCommand.ToUpperInvariant()}");
        await Console.Out.WriteLineAsync($"{completionMarker}0");
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

static bool TryHandleWrappedCommand(string line, out string decodedCommand, out string completionMarker)
{
    const string base64Prefix = "FromBase64String('";
    const string markerPrefix = "Write-Host ('";
    const string markerSuffix = "' + $__agenticterminal_exit)";

    decodedCommand = string.Empty;
    completionMarker = string.Empty;

    var base64Start = line.IndexOf(base64Prefix, StringComparison.Ordinal);
    if (base64Start < 0)
    {
        return false;
    }

    base64Start += base64Prefix.Length;
    var base64End = line.IndexOf("')", base64Start, StringComparison.Ordinal);
    if (base64End < 0)
    {
        return false;
    }

    var markerStart = line.IndexOf(markerPrefix, StringComparison.Ordinal);
    if (markerStart < 0)
    {
        return false;
    }

    markerStart += markerPrefix.Length;
    var markerEnd = line.IndexOf(markerSuffix, markerStart, StringComparison.Ordinal);
    if (markerEnd < 0)
    {
        return false;
    }

    completionMarker = line[markerStart..markerEnd];
    decodedCommand = Encoding.UTF8.GetString(Convert.FromBase64String(line[base64Start..base64End]));
    return true;
}

static string Escape(string text)
{
    return text
        .Replace("\r", "<CR>", StringComparison.Ordinal)
        .Replace("\n", "<LF>", StringComparison.Ordinal);
}

public sealed class TestHostMarker;
