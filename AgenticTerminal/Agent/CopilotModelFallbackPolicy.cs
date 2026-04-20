namespace AgenticTerminal.Agent;

public static class CopilotModelFallbackPolicy
{
    public static bool ShouldFallbackToDefaultModel(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Message.Contains("model", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("not available", StringComparison.OrdinalIgnoreCase);
    }
}
