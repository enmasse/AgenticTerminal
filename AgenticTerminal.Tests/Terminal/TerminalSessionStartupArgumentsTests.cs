using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalSessionStartupArgumentsTests
{
    [Fact]
    public void BuildShellArguments_WithoutPromptSuppression_UsesInteractivePrompt()
    {
        var arguments = TerminalSessionStartupArguments.Build(new TerminalSessionStartupOptions());

        Assert.Contains("-NoLogo", arguments);
        Assert.Contains("-NoExit", arguments);
        Assert.DoesNotContain("function prompt", arguments, StringComparison.Ordinal);
        Assert.Contains("$PSStyle.OutputRendering='Ansi'", arguments);
        Assert.DoesNotContain("-NoProfile", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildShellArguments_WithPromptSuppression_DisablesPromptFunction()
    {
        var arguments = TerminalSessionStartupArguments.Build(new TerminalSessionStartupOptions(TerminalSessionMode.InteractivePseudoConsole, SuppressPrompt: true));

        Assert.Contains("function prompt { '' }", arguments);
        Assert.Contains("[Console]::InputEncoding=[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)", arguments);
    }

    [Fact]
    public void BuildShellArguments_WithHeadlessMode_UsesPlainTextOutput()
    {
        var arguments = TerminalSessionStartupArguments.Build(new TerminalSessionStartupOptions(TerminalSessionMode.HeadlessPipe, LoadUserProfile: false));

        Assert.Contains("-NoExit", arguments);
        Assert.Contains("-NoProfile", arguments);
        Assert.Contains("$PSStyle.OutputRendering='PlainText'", arguments);
        Assert.DoesNotContain("function prompt", arguments, StringComparison.Ordinal);
    }
}
