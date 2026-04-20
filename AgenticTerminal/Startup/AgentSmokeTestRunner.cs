using AgenticTerminal.Agent;

namespace AgenticTerminal.Startup;

public static class AgentSmokeTestRunner
{
    public static async Task RunAsync(CopilotAgentSessionManager sessionManager, AppCommandLineOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync(FormatSection("Smoke test prompt:", options.SmokeTestPrompt));
        await output.WriteLineAsync();

        var result = await sessionManager.SendPromptForResponseAsync(options.SmokeTestPrompt, cancellationToken);
        var response = result.GetAssistantResponseOrThrow();

        await output.WriteLineAsync(FormatSection("Smoke test response:", response));
    }

    public static string FormatSection(string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        var sanitizedBody = SmokeTestOutputSanitizer.Sanitize(body).Trim('\n');
        return string.IsNullOrEmpty(sanitizedBody)
            ? title
            : $"{title}\n{sanitizedBody}";
    }
}
