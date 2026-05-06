namespace AgenticTerminal.Agent;

public sealed record CopilotSessionOptions(
    TimeSpan? FirstTokenTimeout,
    Func<string, CancellationToken, Task>? PreferredModelChanged = null)
{
    public static CopilotSessionOptions Default { get; } = new((TimeSpan?)null);
}
