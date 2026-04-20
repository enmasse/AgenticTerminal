using AgenticTerminal.Agent;

namespace AgenticTerminal.Tests.Agent;

public sealed class ConversationTranscriptTests
{
    [Fact]
    public void AppendAssistantDelta_BuildsSingleAssistantMessage()
    {
        var transcript = new ConversationTranscript();

        transcript.AddUserMessage("Summarize the repository");
        transcript.AppendAssistantDelta("Hello");
        transcript.AppendAssistantDelta(" world");
        transcript.CompleteAssistantMessage();

        Assert.Collection(
            transcript.Messages,
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal("Summarize the repository", user.Content);
            },
            assistant =>
            {
                Assert.Equal("assistant", assistant.Role);
                Assert.Equal("Hello world", assistant.Content);
            });
    }

    [Fact]
    public void CompleteAssistantMessage_PrefersFinalContentOverAccumulatedDelta()
    {
        var transcript = new ConversationTranscript();

        transcript.AppendAssistantDelta("Partial");
        transcript.CompleteAssistantMessage("Final response");

        var message = Assert.Single(transcript.Messages);
        Assert.Equal("assistant", message.Role);
        Assert.Equal("Final response", message.Content);
    }
}
