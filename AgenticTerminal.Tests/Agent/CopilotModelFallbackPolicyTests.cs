using AgenticTerminal.Agent;

namespace AgenticTerminal.Tests.Agent;

public sealed class CopilotModelFallbackPolicyTests
{
    [Theory]
    [InlineData("Communication error with Copilot CLI: Request session.create failed with message: Model \"gpt-5\" is not available.")]
    [InlineData("Model 'o4-custom' is not available for this account.")]
    public void ShouldFallbackToDefaultModel_WhenModelIsUnavailable(string errorMessage)
    {
        var shouldFallback = CopilotModelFallbackPolicy.ShouldFallbackToDefaultModel(new InvalidOperationException(errorMessage));

        Assert.True(shouldFallback);
    }

    [Fact]
    public void ShouldFallbackToDefaultModel_ReturnsFalseForOtherFailures()
    {
        var shouldFallback = CopilotModelFallbackPolicy.ShouldFallbackToDefaultModel(new InvalidOperationException("Network timeout."));

        Assert.False(shouldFallback);
    }
}
