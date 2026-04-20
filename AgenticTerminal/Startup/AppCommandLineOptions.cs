namespace AgenticTerminal.Startup;

public sealed record AppCommandLineOptions(bool RunSmokeTest, string SmokeTestPrompt, int SmokeTestTimeoutSeconds, string? CopilotModel)
{
    public static AppCommandLineOptions Interactive { get; } = new(false, "Reply with the word READY.", 30, null);
}
