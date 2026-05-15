using AgenticTerminal.Startup;
using AgenticTerminal.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

return AppEntry.Main(args);

internal static class AppEntry
{
    [System.STAThread]
    internal static int Main(string[] args)
    {
        int hex1bResult = 0;

        var hex1bThread = new Thread(() =>
            hex1bResult = AppHost.RunAsync(args).GetAwaiter().GetResult())
        {
            IsBackground = false,
            Name = "Hex1bHost"
        };
        hex1bThread.Start();

        AppBuilder.Configure<AvaloniaPanelApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);

        hex1bThread.Join();
        return hex1bResult;
    }
}

