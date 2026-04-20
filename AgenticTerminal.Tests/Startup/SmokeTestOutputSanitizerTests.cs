using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class SmokeTestOutputSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesAnsiEscapeSequences()
    {
        var sanitized = SmokeTestOutputSanitizer.Sanitize("\u001b[32mReady\u001b[0m to help!");

        Assert.Equal("Ready to help!", sanitized);
    }

    [Fact]
    public void Sanitize_RemovesOverwrittenPrefixFromCarriageReturnContent()
    {
        var sanitized = SmokeTestOutputSanitizer.Sanitize("Ready\rPrompt> ");

        Assert.Equal("Prompt> ", sanitized);
    }

    [Fact]
    public void Sanitize_NormalizesMixedControlCharactersWhileKeepingLineBreaks()
    {
        var sanitized = SmokeTestOutputSanitizer.Sanitize("Line 1\r\n\u0007Line 2\nLine\t3");

        Assert.Equal("Line 1\nLine 2\nLine\t3", sanitized);
    }
}
