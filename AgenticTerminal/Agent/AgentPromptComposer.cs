namespace AgenticTerminal.Agent;

public static class AgentPromptComposer
{
    public static string Compose(string prompt, string? terminalSnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        if (string.IsNullOrWhiteSpace(terminalSnapshot))
        {
            return prompt;
        }

        return string.Join(
            "\n\n",
            "Terminal snapshot before this prompt:\n" + terminalSnapshot.Trim(),
            "Use the terminal snapshot as context when deciding what to do next.",
            "User prompt:\n" + prompt);
    }
}
