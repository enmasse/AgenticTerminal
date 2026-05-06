namespace AgenticTerminal.Agent;

internal static class TrackedCommandBuilder
{
    private static readonly HashSet<string> PowerShellBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd",
        "chdir",
        "cls",
        "clear-host",
        "dir",
        "echo",
        "exit",
        "foreach-object",
        "set-location",
        "where-object",
        "write-host"
    };

    public static bool TryWrap(string sessionId, string command, out string wrappedCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        wrappedCommand = command;
        if (ContainsShellOwnedSyntax(command) || !TryTokenize(command, out var tokens) || tokens.Count == 0)
        {
            return false;
        }

        var executable = tokens[0];
        if (IsPowerShellOwnedCommand(executable))
        {
            return false;
        }

        wrappedCommand = string.Join(' ',
            [
                "agt",
                $"--session={FormatArgument(sessionId)}",
                FormatArgument(executable),
                .. tokens.Skip(1).Select(FormatArgument)
            ]);
        return true;
    }

    private static bool ContainsShellOwnedSyntax(string command)
    {
        var inQuotes = false;
        for (var index = 0; index < command.Length; index++)
        {
            var current = command[index];
            if (current == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && (current is '|' or '>' or '<' or ';' or '&'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryTokenize(string command, out IReadOnlyList<string> tokens)
    {
        var results = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in command)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    results.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            tokens = [];
            return false;
        }

        if (current.Length > 0)
        {
            results.Add(current.ToString());
        }

        tokens = results;
        return true;
    }

    private static bool IsPowerShellOwnedCommand(string executable)
    {
        if (PowerShellBuiltIns.Contains(executable))
        {
            return true;
        }

        var hyphenIndex = executable.IndexOf('-');
        if (hyphenIndex <= 0 || hyphenIndex == executable.Length - 1)
        {
            return false;
        }

        return char.IsUpper(executable[0]) && char.IsUpper(executable[hyphenIndex + 1]);
    }

    private static string FormatArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.IndexOfAny([' ', '\t', '"']) >= 0
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }
}
