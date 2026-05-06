using System.Reflection;
using AgenticTerminal.UI;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bApplicationShellFocusTests
{
    [Theory]
    [InlineData(Hex1bFocusTarget.Approval, false)]
    [InlineData(Hex1bFocusTarget.UserInput, true)]
    [InlineData(Hex1bFocusTarget.Prompt, false)]
    [InlineData(Hex1bFocusTarget.Terminal, true)]
    [InlineData(Hex1bFocusTarget.Sessions, true)]
    public void ShouldRestorePromptFocusAfterAgentUpdate_ReturnsExpectedValue(Hex1bFocusTarget focusTarget, bool expected)
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "ShouldRestorePromptFocusAfterAgentUpdate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, [focusTarget])!);
    }
}
