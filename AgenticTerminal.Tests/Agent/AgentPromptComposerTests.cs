using AgenticTerminal.Agent;

namespace AgenticTerminal.Tests.Agent;

public sealed class AgentPromptComposerTests
{
    [Fact]
    public void Compose_WithTerminalSnapshot_AppendsSnapshotContextBeforeUserPrompt()
    {
        var composed = AgentPromptComposer.Compose(
            "list the files in the current folder",
            "Cursor: row 3, column 4\nVisible screen:\n01| PS> git status\n02| On branch main");

        Assert.Equal(
            "Terminal snapshot before this prompt:\nCursor: row 3, column 4\nVisible screen:\n01| PS> git status\n02| On branch main\n\nUse the terminal snapshot as context when deciding what to do next.\n\nUser prompt:\nlist the files in the current folder",
            composed);
    }

    [Fact]
    public void Compose_WithoutTerminalSnapshot_ReturnsOriginalPrompt()
    {
        var composed = AgentPromptComposer.Compose("show the last build output", "   ");

        Assert.Equal("show the last build output", composed);
    }
}
