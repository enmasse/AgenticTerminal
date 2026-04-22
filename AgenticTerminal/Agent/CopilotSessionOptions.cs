namespace AgenticTerminal.Agent;

public sealed record CopilotSessionOptions(TimeSpan? FirstTokenTimeout)
{
    public static CopilotSessionOptions Default { get; } = new((TimeSpan?)null);
}
