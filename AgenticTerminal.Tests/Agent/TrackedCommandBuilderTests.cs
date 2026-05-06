using AgenticTerminal.Agent;

namespace AgenticTerminal.Tests.Agent;

public sealed class TrackedCommandBuilderTests
{
    [Fact]
    public void TryWrapExecutableCommand_WithDirectExecutable_BuildsAgtInvocation()
    {
        var wrapped = TrackedCommandBuilder.TryWrap("session-42", "git status", out var command);

        Assert.True(wrapped);
        Assert.Equal("agt --session=session-42 git status", command);
    }

    [Fact]
    public void TryWrapExecutableCommand_WithQuotedArguments_PreservesArgumentBoundaries()
    {
        var wrapped = TrackedCommandBuilder.TryWrap("session-42", "dotnet test \"My Tests.csproj\"", out var command);

        Assert.True(wrapped);
        Assert.Equal("agt --session=session-42 dotnet test \"My Tests.csproj\"", command);
    }

    [Theory]
    [InlineData("git status | findstr main")]
    [InlineData("Get-Process")]
    [InlineData("Write-Host hi")]
    public void TryWrapExecutableCommand_WithShellOwnedSyntax_FallsBackToOriginalCommand(string originalCommand)
    {
        var wrapped = TrackedCommandBuilder.TryWrap("session-42", originalCommand, out var command);

        Assert.False(wrapped);
        Assert.Equal(originalCommand, command);
    }
}
