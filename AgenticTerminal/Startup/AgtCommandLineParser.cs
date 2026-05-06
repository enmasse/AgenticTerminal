namespace AgenticTerminal.Startup;

internal static class AgtCommandLineParser
{
    public static AgtCommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? sessionId = null;
        var isScriptMode = false;
        var remainingArguments = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "script", StringComparison.Ordinal) && remainingArguments.Count == 0)
            {
                isScriptMode = true;
                continue;
            }

            if (string.Equals(argument, "--", StringComparison.Ordinal))
            {
                remainingArguments.AddRange(args[(index + 1)..]);
                break;
            }

            if (argument.StartsWith("--session=", StringComparison.Ordinal))
            {
                sessionId = argument[10..];
                continue;
            }

            if (string.Equals(argument, "--session", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("The --session option requires a value.");
                }

                sessionId = args[++index];
                continue;
            }

            remainingArguments.AddRange(args[index..]);
            break;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("The agt wrapper requires a --session option.");
        }

        if (remainingArguments.Count == 0)
        {
            throw new ArgumentException("The agt wrapper requires a command to run.");
        }

        if (isScriptMode)
        {
            return new AgtCommandLineOptions(sessionId, true, "pwsh.exe", ["-NoLogo", "-NoProfile", "-Command", string.Join(' ', remainingArguments)]);
        }

        return new AgtCommandLineOptions(sessionId, false, remainingArguments[0], remainingArguments.Skip(1).ToArray());
    }
}
