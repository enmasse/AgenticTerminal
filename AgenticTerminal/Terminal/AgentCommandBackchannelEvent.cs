using ProtoBuf;

namespace AgenticTerminal.Terminal;

[ProtoContract]
internal sealed class AgentCommandBackchannelEvent
{
    [ProtoMember(1)]
    public string SessionId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string CommandId { get; set; } = string.Empty;

    [ProtoMember(3)]
    public AgentCommandBackchannelEventType EventType { get; set; }

    [ProtoMember(4)]
    public string CommandText { get; set; } = string.Empty;

    [ProtoMember(5)]
    public int ProcessId { get; set; }

    [ProtoMember(6)]
    public long StartedAtUnixTimeMilliseconds { get; set; }

    [ProtoMember(7)]
    public long CompletedAtUnixTimeMilliseconds { get; set; }

    [ProtoMember(8)]
    public int ExitCode { get; set; }

    [ProtoMember(9)]
    public string StandardOutputTail { get; set; } = string.Empty;

    [ProtoMember(10)]
    public string StandardErrorTail { get; set; } = string.Empty;
}

internal enum AgentCommandBackchannelEventType
{
    Unknown = 0,
    Started = 1,
    Completed = 2
}

internal enum AgentCommandBufferStream
{
    StandardOutput,
    StandardError
}

internal sealed record AgentCommandBackchannelSummary(
    string CommandId,
    AgentCommandBackchannelEventType EventType,
    string CommandText,
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan? Duration,
    int? ExitCode);

internal sealed record AgentCommandBufferTail(IReadOnlyList<string> Lines);

internal sealed record AgentCommandRecord(
    string CommandId,
    string SessionId,
    string CommandText,
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan? Duration,
    int? ExitCode,
    string StandardOutputTail,
    string StandardErrorTail,
    bool IsCompleted);
