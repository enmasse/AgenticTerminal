namespace AgenticTerminal.Startup;

internal sealed record AgtCommandLineOptions(
    string SessionId,
    bool IsScriptMode,
    string Command,
    IReadOnlyList<string> Arguments)
{
    public string DisplayCommand => string.Join(' ', [Command, .. Arguments.Select(FormatArgument)]).Trim();

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
