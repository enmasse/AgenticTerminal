using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class AppModeResolverTests
{
    [Fact]
    public void Resolve_WithCanonicalExecutable_UsesInteractiveMode()
    {
        var mode = AppModeResolver.Resolve("AgenticTerminal.exe", []);

        Assert.Equal(AppMode.Interactive, mode);
    }

    [Fact]
    public void Resolve_WithAgtAliasWithoutArguments_UsesInteractiveMode()
    {
        var mode = AppModeResolver.Resolve("agt.exe", []);

        Assert.Equal(AppMode.Interactive, mode);
    }

    [Fact]
    public void Resolve_WithAgtAliasAndSessionArgument_UsesWrapperMode()
    {
        var mode = AppModeResolver.Resolve("agt.exe", ["--session=session-42", "git", "status"]);

        Assert.Equal(AppMode.Wrapper, mode);
    }

    [Fact]
    public void Resolve_WithAgtAliasAndScriptSubcommand_UsesInteractiveModeUntilSessionIsProvided()
    {
        var mode = AppModeResolver.Resolve("agt.exe", ["script", "Write-Host", "hi"]);

        Assert.Equal(AppMode.Interactive, mode);
    }
}
