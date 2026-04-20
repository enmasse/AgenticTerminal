using AgenticTerminal.Agent;

namespace AgenticTerminal.Tests.Agent;

public sealed class PromptCompletionResultTests
{
    [Fact]
    public void GetAssistantResponseOrThrow_ReturnsCombinedAssistantContent()
    {
        var result = new PromptCompletionResult(true, ["First answer", "Second answer"], null);

        Assert.Equal("First answer\n\nSecond answer", result.GetAssistantResponseOrThrow());
    }

    [Fact]
    public void GetAssistantResponseOrThrow_ThrowsWhenPromptFails()
    {
        var result = new PromptCompletionResult(false, [], "Copilot session failed.");

        var exception = Assert.Throws<InvalidOperationException>(() => result.GetAssistantResponseOrThrow());

        Assert.Equal("Copilot session failed.", exception.Message);
    }
}
