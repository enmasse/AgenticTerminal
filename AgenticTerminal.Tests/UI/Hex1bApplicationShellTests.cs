using System.Reflection;
using AgenticTerminal.UI;
using Hex1b;

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

    [Fact]
    public void CreateAppOptions_EnablesMouse()
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "CreateAppOptions",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var options = (Hex1bAppOptions?)method!.Invoke(null, null);

        Assert.NotNull(options);
        Assert.True(options!.EnableMouse);
    }

    [Fact]
    public void ConfigureTerminalBuilder_EnablesMouse()
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "ConfigureTerminalBuilder",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var builder = new Hex1bTerminalBuilder();
        var configuredBuilder = method!.Invoke(null, [builder]);

        Assert.Same(builder, configuredBuilder);

        var enableMouseField = typeof(Hex1bTerminalBuilder).GetField(
            "_enableMouse",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(enableMouseField);
        Assert.True((bool)enableMouseField!.GetValue(builder)!);
    }
}
