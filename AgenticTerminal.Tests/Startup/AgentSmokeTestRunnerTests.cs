using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class AgentSmokeTestRunnerTests
{
    [Fact]
    public void FormatSection_WritesSanitizedBody()
    {
        var formatted = AgentSmokeTestRunner.FormatSection("Smoke test response:", "\u001b[32mReady\u001b[0m\rPrompt> ");

        Assert.Equal("Smoke test response:\nPrompt> ", formatted);
    }
}
