namespace AgenticTerminal.Agent;

public sealed class AgentUserInputRequestState
{
    public string Question { get; init; } = string.Empty;

    public IReadOnlyList<string> Choices { get; init; } = [];

    public bool AllowFreeformInput { get; init; }

    public string PromptText { get; set; } = string.Empty;

    public int SelectedChoiceIndex { get; set; }

    public bool HasChoices => Choices.Count > 0;
}
