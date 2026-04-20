using System.Text;
using AgenticTerminal.Persistence;

namespace AgenticTerminal.Agent;

public sealed class ConversationTranscript
{
    private readonly List<ConversationMessage> _messages = [];
    private StringBuilder? _assistantBuffer;
    private DateTimeOffset _assistantTimestamp;

    public IReadOnlyList<ConversationMessage> Messages => _messages;

    public void Load(IEnumerable<ConversationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages.Clear();
        _messages.AddRange(messages);
        _assistantBuffer = null;
        _assistantTimestamp = default;
    }

    public void Clear()
    {
        _messages.Clear();
        _assistantBuffer = null;
        _assistantTimestamp = default;
    }

    public void AddUserMessage(string content, DateTimeOffset? timestamp = null)
    {
        AddMessage("user", content, timestamp);
    }

    public void AppendAssistantDelta(string delta, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        _assistantBuffer ??= new StringBuilder();
        if (_assistantTimestamp == default)
        {
            _assistantTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        }

        _assistantBuffer.Append(delta);
    }

    public void CompleteAssistantMessage(string? finalContent = null, DateTimeOffset? timestamp = null)
    {
        var content = finalContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            content = _assistantBuffer?.ToString();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _assistantBuffer = null;
            _assistantTimestamp = default;
            return;
        }

        var messageTimestamp = timestamp ?? (_assistantTimestamp == default ? DateTimeOffset.UtcNow : _assistantTimestamp);
        _messages.Add(new ConversationMessage("assistant", content, messageTimestamp));
        _assistantBuffer = null;
        _assistantTimestamp = default;
    }

    private void AddMessage(string role, string content, DateTimeOffset? timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        _messages.Add(new ConversationMessage(role, content, timestamp ?? DateTimeOffset.UtcNow));
    }
}
