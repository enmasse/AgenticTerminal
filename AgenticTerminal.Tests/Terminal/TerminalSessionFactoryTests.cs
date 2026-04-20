using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalSessionFactoryTests
{
    [Fact]
    public void Create_WithInteractiveMode_ReturnsHex1bPtySession()
    {
        var session = TerminalSessionFactory.Create(new TerminalSessionStartupOptions(TerminalSessionMode.InteractivePseudoConsole));

        Assert.IsType<Hex1bPtyTerminalSession>(session);
    }

    [Fact]
    public void Create_WithHeadlessMode_ReturnsHeadlessSession()
    {
        var session = TerminalSessionFactory.Create(new TerminalSessionStartupOptions(TerminalSessionMode.HeadlessPipe));

        Assert.IsType<HeadlessPowerShellTerminalSession>(session);
    }

    [Fact]
    public void BuildArgumentList_KeepsInitializationScriptAsSingleArgument()
    {
        var arguments = TerminalSessionStartupArguments.BuildArgumentList(
            new TerminalSessionStartupOptions(TerminalSessionMode.InteractivePseudoConsole, SuppressPrompt: true, LoadUserProfile: false));

        Assert.Equal("-NoLogo", arguments[0]);
        Assert.Contains("-NoProfile", arguments);
        Assert.Equal("-Command", arguments[^2]);
        Assert.Contains("function prompt { '' }", arguments[^1]);
        Assert.Contains("$PSStyle.OutputRendering='Ansi'", arguments[^1]);
    }
}
