using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;

namespace AgenticTerminal.Terminal;

internal sealed class AgentCommandBackchannelSession : IAsyncDisposable, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentDictionary<string, AgentCommandState> _commands = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedCommandOrder = new();
    private readonly List<AgentCommandBackchannelSummary> _eventLog = [];
    private readonly int _completedBufferCapacity;
    private readonly Task _listenerTask;

    public AgentCommandBackchannelSession(string sessionId, int completedBufferCapacity = 16)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("A session id is required.", nameof(sessionId));
        }

        if (completedBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedBufferCapacity));
        }

        SessionId = sessionId;
        PipeName = BuildPipeName(sessionId);
        _completedBufferCapacity = completedBufferCapacity;
        _listenerTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
    }

    public string SessionId { get; }

    public string PipeName { get; }

    public long CreateCursor()
    {
        lock (_syncRoot)
        {
            return _eventLog.Count;
        }
    }

    public IReadOnlyList<AgentCommandBackchannelSummary> ReadEventsSince(long cursor)
    {
        lock (_syncRoot)
        {
            if (cursor < 0 || cursor >= _eventLog.Count)
            {
                return cursor == _eventLog.Count ? [] : _eventLog.ToArray();
            }

            return _eventLog[(int)cursor..].ToArray();
        }
    }

    public void ObserveEvent(AgentCommandBackchannelEvent backchannelEvent)
    {
        ArgumentNullException.ThrowIfNull(backchannelEvent);
        if (!string.Equals(backchannelEvent.SessionId, SessionId, StringComparison.Ordinal))
        {
            return;
        }

        var startedAt = backchannelEvent.StartedAtUnixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(backchannelEvent.StartedAtUnixTimeMilliseconds)
            : (DateTimeOffset?)null;
        var completedAt = backchannelEvent.CompletedAtUnixTimeMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(backchannelEvent.CompletedAtUnixTimeMilliseconds)
            : (DateTimeOffset?)null;

        var sessionId = SessionId;
        var state = _commands.GetOrAdd(backchannelEvent.CommandId, commandId => new AgentCommandState(commandId, sessionId));
        lock (_syncRoot)
        {
            state.CommandText = string.IsNullOrWhiteSpace(backchannelEvent.CommandText) ? state.CommandText : backchannelEvent.CommandText;
            state.ProcessId = backchannelEvent.ProcessId != 0 ? backchannelEvent.ProcessId : state.ProcessId;
            state.StartedAt = startedAt ?? state.StartedAt;

            if (backchannelEvent.EventType == AgentCommandBackchannelEventType.Completed)
            {
                state.CompletedAt = completedAt;
                state.ExitCode = backchannelEvent.ExitCode;
                state.StandardOutputTail = backchannelEvent.StandardOutputTail ?? string.Empty;
                state.StandardErrorTail = backchannelEvent.StandardErrorTail ?? string.Empty;
                state.IsCompleted = true;
                _completedCommandOrder.Enqueue(backchannelEvent.CommandId);
                TrimCompletedCommandsUnsafe();
            }

            _eventLog.Add(new AgentCommandBackchannelSummary(
                backchannelEvent.CommandId,
                backchannelEvent.EventType,
                state.CommandText,
                state.ProcessId,
                state.StartedAt,
                state.CompletedAt,
                state.StartedAt is { } eventStartedAt && state.CompletedAt is { } eventCompletedAt
                    ? eventCompletedAt - eventStartedAt
                    : null,
                backchannelEvent.EventType == AgentCommandBackchannelEventType.Completed ? backchannelEvent.ExitCode : null));
        }
    }

    public AgentCommandRecord? TryGetCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId) || !_commands.TryGetValue(commandId, out var state))
        {
            return null;
        }

        lock (_syncRoot)
        {
            return state.ToRecord();
        }
    }

    public AgentCommandBufferTail ReadBufferTail(string commandId, AgentCommandBufferStream stream, int maxLines)
    {
        if (maxLines <= 0)
        {
            return new AgentCommandBufferTail([]);
        }

        if (!_commands.TryGetValue(commandId, out var state))
        {
            throw new InvalidOperationException($"No wrapped command buffer was found for '{commandId}'.");
        }

        lock (_syncRoot)
        {
            var text = stream == AgentCommandBufferStream.StandardError ? state.StandardErrorTail : state.StandardOutputTail;
            var lines = text.ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(maxLines)
                .ToArray();
            return new AgentCommandBufferTail(lines);
        }
    }

    public static string BuildPipeName(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return $"agenticterminal-session-{sessionId}";
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        try
        {
            await _listenerTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(server, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var _ = stream;
        var lengthBuffer = new byte[sizeof(int)];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await TryReadExactAsync(stream, lengthBuffer, cancellationToken))
            {
                return;
            }

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (payloadLength <= 0)
            {
                return;
            }

            var payload = new byte[payloadLength];
            if (!await TryReadExactAsync(stream, payload, cancellationToken))
            {
                return;
            }

            using var memoryStream = new MemoryStream(payload, writable: false);
            var backchannelEvent = AgentCommandBackchannelEvent.Parser.ParseFrom(memoryStream);
            ObserveEvent(backchannelEvent);
        }
    }

    private void TrimCompletedCommandsUnsafe()
    {
        while (_completedCommandOrder.Count > _completedBufferCapacity)
        {
            var candidateId = _completedCommandOrder.Dequeue();
            if (_commands.TryGetValue(candidateId, out var state) && state.IsCompleted)
            {
                _commands.TryRemove(candidateId, out _);
            }
        }
    }

    private static async Task<bool> TryReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                return false;
            }

            totalRead += bytesRead;
        }

        return true;
    }

    private sealed class AgentCommandState(string commandId, string sessionId)
    {
        public string CommandId { get; } = commandId;

        public string SessionId { get; } = sessionId;

        public string CommandText { get; set; } = string.Empty;

        public int ProcessId { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public int? ExitCode { get; set; }

        public string StandardOutputTail { get; set; } = string.Empty;

        public string StandardErrorTail { get; set; } = string.Empty;

        public bool IsCompleted { get; set; }

        public AgentCommandRecord ToRecord()
        {
            return new AgentCommandRecord(
                CommandId,
                SessionId,
                CommandText,
                ProcessId,
                StartedAt,
                CompletedAt,
                StartedAt is { } startedAt && CompletedAt is { } completedAt ? completedAt - startedAt : null,
                ExitCode,
                StandardOutputTail,
                StandardErrorTail,
                IsCompleted);
        }
    }
}
