using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class AppCommandLineOptionsModelTests
{
    [Fact]
    public void Parse_WithoutModel_LeavesModelUnset()
    {
        var options = AppCommandLineOptionsParser.Parse([]);

        Assert.Null(options.CopilotModel);
    }

    [Fact]
    public void Parse_WithModel_UsesProvidedValue()
    {
        var options = AppCommandLineOptionsParser.Parse(["--model", "claude-sonnet-4.5"]);

        Assert.Equal("claude-sonnet-4.5", options.CopilotModel);
    }

    [Fact]
    public void Parse_WithMissingModelValue_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => AppCommandLineOptionsParser.Parse(["--model"]));

        Assert.Contains("--model", exception.Message);
    }
}
