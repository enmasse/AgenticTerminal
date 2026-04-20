namespace AgenticTerminal.Agent;

public sealed record PromptCompletionResult(bool Succeeded, IReadOnlyList<string> AssistantMessages, string? ErrorMessage)
{
    public string GetAssistantResponseOrThrow()
    {
        if (!Succeeded)
        {
            throw new InvalidOperationException(ErrorMessage ?? "The prompt did not complete successfully.");
        }

        return string.Join("\n\n", AssistantMessages);
    }
}
