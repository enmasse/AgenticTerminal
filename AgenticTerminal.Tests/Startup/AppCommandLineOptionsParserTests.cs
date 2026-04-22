using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class AppCommandLineOptionsParserTests
{
    [Fact]
    public void Parse_WithoutArguments_UsesInteractiveMode()
    {
        var options = AppCommandLineOptionsParser.Parse([]);

        Assert.False(options.RunSmokeTest);
        Assert.Equal(30, options.SmokeTestTimeoutSeconds);
        Assert.Equal("Reply with the word READY.", options.SmokeTestPrompt);
    }

    [Fact]
    public void Parse_WithSmokeTestArguments_UsesProvidedPromptAndTimeout()
    {
        var options = AppCommandLineOptionsParser.Parse(["--smoke-test", "What is 2+2?", "--smoke-test-timeout", "45"]);

        Assert.True(options.RunSmokeTest);
        Assert.Equal("What is 2+2?", options.SmokeTestPrompt);
        Assert.Equal(45, options.SmokeTestTimeoutSeconds);
    }

    [Fact]
    public void Parse_WithMissingSmokeTestPrompt_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => AppCommandLineOptionsParser.Parse(["--smoke-test"]));

        Assert.Contains("--smoke-test", exception.Message);
    }

    [Fact]
    public void Parse_WithConfigurationDefaults_UsesConfiguredModel()
    {
        var defaults = AppCommandLineOptions.Interactive.ApplyConfiguration(new AppConfiguration("gpt-5"));

        var options = AppCommandLineOptionsParser.Parse([], defaults);

        Assert.Equal("gpt-5", options.CopilotModel);
    }

    [Fact]
    public void Parse_WithCommandLineModel_OverridesConfiguredModel()
    {
        var defaults = AppCommandLineOptions.Interactive.ApplyConfiguration(new AppConfiguration("gpt-5"));

        var options = AppCommandLineOptionsParser.Parse(["--model", "claude-sonnet-4.5"], defaults);

        Assert.Equal("claude-sonnet-4.5", options.CopilotModel);
    }
}
