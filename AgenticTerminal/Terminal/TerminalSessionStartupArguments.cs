namespace AgenticTerminal.Terminal;

public static class TerminalSessionStartupArguments
{
    public static string Build(TerminalSessionStartupOptions options)
    {
        return string.Join(' ', BuildArgumentList(options).Select(Quote));
    }

    public static string[] BuildArgumentList(TerminalSessionStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>
        {
            "-NoLogo"
        };

        if (!options.LoadUserProfile)
        {
            arguments.Add("-NoProfile");
        }

        var initializationCommands = new List<string>();
        if (options.SuppressPrompt)
        {
            initializationCommands.Add("function prompt { '' }");
        }

        initializationCommands.Add(options.Mode == TerminalSessionMode.HeadlessPipe
            ? "$PSStyle.OutputRendering='PlainText'"
            : "$PSStyle.OutputRendering='Ansi'");
        initializationCommands.Add("[Console]::InputEncoding=[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)");

        arguments.AddRange(["-NoExit", "-Command", string.Join("; ", initializationCommands)]);
        return [.. arguments];
    }

    private static string Quote(string text)
    {
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
