namespace AgenticTerminal.Startup;

using System.Runtime.InteropServices;

internal static class AgtExecutableBootstrapper
{
    public static IReadOnlyDictionary<string, string> CreateEnvironmentOverrides()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return new Dictionary<string, string>();
        }

        var appDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(appDirectory))
        {
            return new Dictionary<string, string>();
        }

        EnsureAlias(appDirectory, processPath);
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var updatedPath = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(existing => string.Equals(existing, appDirectory, StringComparison.OrdinalIgnoreCase))
            ? currentPath
            : string.Join(Path.PathSeparator, [appDirectory, currentPath]);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = updatedPath
        };
    }

    private static void EnsureAlias(string appDirectory, string processPath)
    {
        var aliasPath = Path.Combine(appDirectory, "agt" + Path.GetExtension(processPath));
        if (string.Equals(aliasPath, processPath, StringComparison.OrdinalIgnoreCase) || File.Exists(aliasPath))
        {
            return;
        }

        if (!CreateHardLink(aliasPath, processPath, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Failed to create the agt hard link at '{aliasPath}'.");
        }
    }

    [DllImport("Kernel32", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
