using System.Reflection;
using AgenticTerminal.Agent;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using AgenticTerminal.Approvals;
using GitHub.Copilot.SDK;

namespace AgenticTerminal.Tests.Agent;

public sealed class CopilotAgentSessionManagerPromptTests
{
    [Fact]
    public void CreateSystemMessage_IncludesAgtWrapperAndCircularBufferGuidance()
    {
        var createMethod = typeof(CopilotAgentSessionManager).GetMethod(
            "CreateSystemMessage",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(createMethod);

        var config = (SystemMessageConfig?)createMethod!.Invoke(null, [Array.Empty<ConversationMessage>()]);

        Assert.NotNull(config);
        Assert.Contains("agt --session=", config!.Content, StringComparison.Ordinal);
        Assert.Contains("read_wrapped_command_buffer", config.Content, StringComparison.Ordinal);
        Assert.Contains("commandId", config.Content, StringComparison.Ordinal);
        Assert.Contains("circular buffer", config.Content, StringComparison.OrdinalIgnoreCase);
    }
}
