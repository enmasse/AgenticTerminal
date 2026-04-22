namespace AgenticTerminal.Startup;

public sealed record AppCommandLineOptions(bool RunSmokeTest, string SmokeTestPrompt, int SmokeTestTimeoutSeconds, string? CopilotModel)
{
    public static AppCommandLineOptions Interactive { get; } = new(false, "Reply with the word READY.", 30, null);

    public AppCommandLineOptions ApplyConfiguration(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return this with
        {
            CopilotModel = string.IsNullOrWhiteSpace(configuration.CopilotModel)
                ? CopilotModel
                : configuration.CopilotModel
        };
    }
}
