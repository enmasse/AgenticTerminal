namespace AgenticTerminal.Terminal;

public static class TerminalSessionFactory
{
    public static ITerminalSession Create(TerminalSessionStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Mode switch
        {
            TerminalSessionMode.HeadlessPipe => new HeadlessPowerShellTerminalSession(options),
            _ => new Hex1bPtyTerminalSession(options)
        };
    }
}
