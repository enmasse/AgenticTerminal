using System.Reflection;
using AgenticTerminal.UI;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bApplicationShellTests
{
    [Theory]
    [InlineData("y", 'y', true)]
    [InlineData("Y", 'y', true)]
    [InlineData("n", 'n', true)]
    [InlineData("N", 'n', true)]
    [InlineData("y", 'n', false)]
    [InlineData("yes", 'y', false)]
    [InlineData("", 'y', false)]
    public void MatchesApprovalInput_MatchesSingleCharacterChoices(string text, char expected, bool result)
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "MatchesApprovalInput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(result, (bool)method!.Invoke(null, [text, expected])!);
    }
}
