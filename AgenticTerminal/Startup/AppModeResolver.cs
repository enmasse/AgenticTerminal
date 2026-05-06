namespace AgenticTerminal.Startup;

internal enum AppMode
{
    Interactive,
    Wrapper
}

internal static class AppModeResolver
{
    public static AppMode Resolve(string? invokedExecutableName, string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!IsAgtAlias(invokedExecutableName) || args.Length == 0)
        {
            return AppMode.Interactive;
        }

        return args.Any(static argument =>
                argument.StartsWith("--session=", StringComparison.Ordinal)
                || string.Equals(argument, "--session", StringComparison.Ordinal))
            ? AppMode.Wrapper
            : AppMode.Interactive;
    }

    private static bool IsAgtAlias(string? invokedExecutableName)
    {
        if (string.IsNullOrWhiteSpace(invokedExecutableName))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(invokedExecutableName);
        return string.Equals(fileName, "agt", StringComparison.OrdinalIgnoreCase);
    }
}
