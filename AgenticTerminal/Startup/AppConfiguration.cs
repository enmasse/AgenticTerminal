namespace AgenticTerminal.Startup;

public sealed record AppConfiguration
{
    public AppConfiguration()
    {
    }

    public AppConfiguration(string? copilotModel)
    {
        CopilotModel = copilotModel;
    }

    public string? CopilotModel { get; init; }

    public int? FirstTokenTimeoutSeconds { get; init; }

    public bool? ShowDebugPanelByDefault { get; init; }

    public static AppConfiguration Empty { get; } = new();
}
