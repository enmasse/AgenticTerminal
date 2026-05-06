using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.AI;

namespace AgenticTerminal.Agent;

public sealed class CopilotAgentSessionManager : IAsyncDisposable
{
    private static readonly TerminalSnapshotOptions SnapshotOptions = new(MaxLines: 12, MaxCharacters: 2000);
    private static readonly TimeSpan DefaultFirstTokenTimeoutDuration = TimeSpan.FromSeconds(15);
    private static readonly string[] AllowedToolNames =
    [
        "powershell",
        "run_terminal_command",
        "read_wrapped_command_events",
        "read_wrapped_command_buffer",
        "read_terminal_snapshot",
        "ask_user"
    ];
    private static readonly IReadOnlyDictionary<string, object?> ToolOverrideProperties = new ReadOnlyDictionary<string, object?>(
        new Dictionary<string, object?>
        {
            ["is_override"] = true
        });

    private readonly ApprovalQueue _approvalQueue;
    private readonly ConversationSessionStore _conversationSessionStore;
    private readonly ConversationTranscript _transcript = new();
    private readonly ITerminalSession _terminalSession;
    private readonly string _workingDirectory;
    private readonly object _promptSyncRoot = new();
    private readonly object _promptTimingSyncRoot = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly TimeSpan _firstTokenTimeout;
    private readonly Func<string, CancellationToken, Task>? _preferredModelChanged;
    private AgentCommandBackchannelSession? _agentCommandBackchannelSession;
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _sessionSubscription;
    private TaskCompletionSource<PromptCompletionResult>? _pendingPromptCompletion;
    private TaskCompletionSource<UserInputResponse>? _pendingUserInputCompletion;
    private CancellationTokenSource? _activePromptTimeoutCancellation;
    private int _assistantMessageCountBeforePrompt;
    private long _activePromptVersion;
    private DateTimeOffset _createdAt;
    private string? _activeSessionId;
    private string _activeTitle = "New session";
    private string? _preferredModel;

    public CopilotAgentSessionManager(
        ApprovalQueue approvalQueue,
        ConversationSessionStore conversationSessionStore,
        ITerminalSession terminalSession,
        string workingDirectory,
        string? preferredModel = null,
        CopilotSessionOptions? options = null)
    {
        _approvalQueue = approvalQueue;
        _conversationSessionStore = conversationSessionStore;
        _terminalSession = terminalSession;
        _workingDirectory = workingDirectory;
        _preferredModel = string.IsNullOrWhiteSpace(preferredModel) ? null : preferredModel;
        _firstTokenTimeout = options?.FirstTokenTimeout is TimeSpan configuredTimeout && configuredTimeout > TimeSpan.Zero
            ? configuredTimeout
            : DefaultFirstTokenTimeoutDuration;
        _preferredModelChanged = options?.PreferredModelChanged;
        _approvalQueue.Changed += HandleStateChanged;
    }

    public event Action? StateChanged;

    public IReadOnlyList<ConversationMessage> Messages => _transcript.Messages;

    public IReadOnlyList<ConversationSessionSummary> SavedSessions { get; private set; } = [];

    public IReadOnlyList<ModelInfo> AvailableModels { get; private set; } = [];

    public ApprovalRequest? PendingApproval => _approvalQueue.Current;

    public AgentToolActivityState? CurrentToolActivity { get; private set; }

    public AgentToolActivityState? LastToolActivity { get; private set; }

    public AgentUserInputRequestState? PendingUserInputRequest { get; private set; }

    public bool HasPendingUserInputRequest => PendingUserInputRequest is not null;

    public AgentPromptTimingState? LatestPromptTiming { get; private set; }

    public TimeSpan FirstTokenTimeout => _firstTokenTimeout;

    public bool IsBusy { get; private set; }

    public string StatusText { get; private set; } = "Starting...";

    public string? ActiveSessionId => _activeSessionId;

    public string? ActiveModelId { get; private set; }

    public double? RemainingQuotaPercentage { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return;
            }

            _client = new CopilotClient(new CopilotClientOptions
            {
                Cwd = _workingDirectory,
                UseLoggedInUser = true,
                AutoStart = true
            });

            await _client.StartAsync();
            await _terminalSession.StartAsync(cancellationToken);

            SavedSessions = await _conversationSessionStore.ListSessionsAsync(cancellationToken);

            if (SavedSessions.Count > 0)
            {
                await OpenStoredSessionCoreAsync(SavedSessions[0].SessionId, cancellationToken);
            }
            else
            {
                await CreateNewSessionCoreAsync(cancellationToken);
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<bool> ChangeModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is null)
            {
                StatusText = "The Copilot session has not been initialized.";
                OnStateChanged();
                return false;
            }

            if (IsBusy)
            {
                StatusText = "Wait for the current response to finish before changing the model.";
                OnStateChanged();
                return false;
            }

            try
            {
                var result = await _session.Rpc.Model.SwitchToAsync(modelId, cancellationToken: cancellationToken);
                _preferredModel = result.ModelId;
                await RefreshModelStateAsync(cancellationToken);
                if (_preferredModelChanged is not null)
                {
                    try
                    {
                        await _preferredModelChanged(result.ModelId, cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        StatusText = $"Model changed to {GetActiveModelDisplayName()}, but the default could not be saved: {exception.Message}";
                        OnStateChanged();
                        return true;
                    }
                }

                StatusText = $"Model changed to {GetActiveModelDisplayName()}.";
                OnStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                StatusText = exception.Message;
                OnStateChanged();
                return false;
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }
    public async Task CreateNewSessionAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await CreateNewSessionCoreAsync(cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task OpenStoredSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await OpenStoredSessionCoreAsync(sessionId, cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await SendPromptCoreAsync(prompt, captureCompletion: false, cancellationToken);
    }

    public async Task<PromptCompletionResult> SendPromptForResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var completionTask = await SendPromptCoreAsync(prompt, captureCompletion: true, cancellationToken);
        return await completionTask.WaitAsync(cancellationToken);
    }

    public Task<bool> ResolvePendingApprovalAsync(bool approved)
    {
        var resolved = _approvalQueue.TryResolveCurrent(approved ? ApprovalDecision.Approved : ApprovalDecision.Denied);
        if (resolved)
        {
            StatusText = approved ? "Command approved" : "Command denied";
            OnStateChanged();
        }

        return Task.FromResult(resolved);
    }

    public Task<bool> SubmitUserInputAsync(string answer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = _pendingUserInputCompletion;
        var request = PendingUserInputRequest;
        if (completion is null || request is null)
        {
            return Task.FromResult(false);
        }

        request.PromptText = answer;
        var resolved = completion.TrySetResult(new UserInputResponse
        {
            Answer = answer,
            WasFreeform = true
        });

        if (resolved)
        {
            ClearPendingUserInputRequest();
            StatusText = "Waiting for Copilot...";
            OnStateChanged();
        }

        return Task.FromResult(resolved);
    }

    public Task<bool> SubmitUserChoiceAsync(int selectedIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = _pendingUserInputCompletion;
        var request = PendingUserInputRequest;
        if (completion is null || request is null || selectedIndex < 0 || selectedIndex >= request.Choices.Count)
        {
            return Task.FromResult(false);
        }

        request.SelectedChoiceIndex = selectedIndex;
        var resolved = completion.TrySetResult(new UserInputResponse
        {
            Answer = request.Choices[selectedIndex],
            WasFreeform = false
        });

        if (resolved)
        {
            ClearPendingUserInputRequest();
            StatusText = "Waiting for Copilot...";
            OnStateChanged();
        }

        return Task.FromResult(resolved);
    }

    public Task<bool> CancelUserInputRequestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = _pendingUserInputCompletion;
        if (completion is null)
        {
            return Task.FromResult(false);
        }

        var resolved = completion.TrySetResult(new UserInputResponse
        {
            Answer = string.Empty,
            WasFreeform = true
        });

        if (resolved)
        {
            ClearPendingUserInputRequest();
            StatusText = "Waiting for Copilot...";
            OnStateChanged();
        }

        return Task.FromResult(resolved);
    }

    public async ValueTask DisposeAsync()
    {
        _approvalQueue.Changed -= HandleStateChanged;
        _sessionSubscription?.Dispose();
        CompletePendingPrompt(new PromptCompletionResult(false, [], "The Copilot session was disposed before the prompt completed."));
        _pendingUserInputCompletion?.TrySetResult(new UserInputResponse
        {
            Answer = string.Empty,
            WasFreeform = true
        });
        ClearPendingUserInputRequest();
        CancelActivePromptTimeout();
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        if (_client is not null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
        }

        if (_agentCommandBackchannelSession is not null)
        {
            await _agentCommandBackchannelSession.DisposeAsync();
        }

        await _terminalSession.DisposeAsync();
        _sessionLock.Dispose();
    }

    private async Task ReplaceSessionAsync(IReadOnlyList<ConversationMessage> existingMessages, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("The Copilot client has not been initialized.");
        }

        _sessionSubscription?.Dispose();
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        try
        {
            _session = await _client.CreateSessionAsync(CreateSessionConfig(existingMessages, _preferredModel));
        }
        catch (Exception exception) when (!string.IsNullOrWhiteSpace(_preferredModel) && CopilotModelFallbackPolicy.ShouldFallbackToDefaultModel(exception))
        {
            StatusText = $"Model '{_preferredModel}' is unavailable. Falling back to Copilot default model.";
            _preferredModel = null;
            OnStateChanged();
            _session = await _client.CreateSessionAsync(CreateSessionConfig(existingMessages, preferredModel: null));
        }

        _sessionSubscription = _session.On(HandleSessionEvent);
        await RefreshModelStateAsync(cancellationToken);
        StatusText = "Ready";
        OnStateChanged();
    }

    private async Task CreateNewSessionCoreAsync(CancellationToken cancellationToken)
    {
        _activeSessionId = Guid.NewGuid().ToString("N");
        _activeTitle = "New session";
        _createdAt = DateTimeOffset.UtcNow;
        _transcript.Clear();
        await ReplaceAgentCommandBackchannelSessionAsync();
        await ReplaceSessionAsync([], cancellationToken);
        await PersistActiveSessionAsync(cancellationToken);
        SavedSessions = await _conversationSessionStore.ListSessionsAsync(cancellationToken);
        StatusText = "Ready";
        OnStateChanged();
    }

    private async Task OpenStoredSessionCoreAsync(string sessionId, CancellationToken cancellationToken)
    {
        var document = await _conversationSessionStore.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"No session found with id '{sessionId}'.");

        _activeSessionId = document.SessionId;
        _activeTitle = document.Title;
        _createdAt = document.CreatedAt;
        _transcript.Load(document.Messages);
        await ReplaceAgentCommandBackchannelSessionAsync();
        await ReplaceSessionAsync(document.Messages, cancellationToken);
        SavedSessions = await _conversationSessionStore.ListSessionsAsync(cancellationToken);
        StatusText = "Ready";
        OnStateChanged();
    }

    private async Task<object> ExecuteTerminalCommandAsync([Description("The PowerShell command to execute.")] string command)
    {
        var decision = await _approvalQueue.EnqueueShellCommandAsync(command);
        if (decision != ApprovalDecision.Approved)
        {
            return new
            {
                approved = false,
                output = "The user denied the command."
            };
        }

        var commandToExecute = !string.IsNullOrWhiteSpace(_activeSessionId)
            && TrackedCommandBuilder.TryWrap(_activeSessionId, command, out var wrappedCommand)
                ? wrappedCommand
                : command;
        var result = await _terminalSession.ExecuteCommandAsync(commandToExecute);
        return new
        {
            approved = true,
            exitCode = result.ExitCode,
            output = result.Output
        };
    }

    private Task<object> ExecutePowerShellToolAsync([Description("The PowerShell command to execute.")] string command)
    {
        return ExecuteTerminalCommandAsync(command);
    }

    private Task<object> ReadWrappedCommandEventsAsync([Description("The maximum number of recent wrapped command events to return.")] int maxEvents = 10)
    {
        var boundedMaxEvents = Math.Clamp(maxEvents, 1, 50);
        var summaries = _agentCommandBackchannelSession?.ReadEventsSince(0)
            .TakeLast(boundedMaxEvents)
            .Select(summary => new
            {
                commandId = summary.CommandId,
                eventType = summary.EventType.ToString(),
                commandText = summary.CommandText,
                pid = summary.ProcessId,
                startedAt = summary.StartedAt,
                completedAt = summary.CompletedAt,
                duration = summary.Duration,
                exitCode = summary.ExitCode
            })
            .ToArray() ?? [];

        return Task.FromResult<object>(new
        {
            sessionId = _activeSessionId,
            events = summaries
        });
    }

    private Task<object> ReadWrappedCommandBufferAsync(
        [Description("The commandId returned by the wrapped command event stream.")] string commandId,
        [Description("The stream to inspect. Use 'stdout' or 'stderr'.")] string stream = "stdout",
        [Description("The maximum number of tail lines to return from the circular buffer.")] int maxLines = 12)
    {
        if (_agentCommandBackchannelSession is null)
        {
            return Task.FromResult<object>(new
            {
                sessionId = _activeSessionId,
                commandId,
                stream,
                lines = Array.Empty<string>()
            });
        }

        var selectedStream = string.Equals(stream, "stderr", StringComparison.OrdinalIgnoreCase)
            ? AgentCommandBufferStream.StandardError
            : AgentCommandBufferStream.StandardOutput;
        var tail = _agentCommandBackchannelSession.ReadBufferTail(commandId, selectedStream, Math.Clamp(maxLines, 1, 50));

        return Task.FromResult<object>(new
        {
            sessionId = _activeSessionId,
            commandId,
            stream = selectedStream == AgentCommandBufferStream.StandardError ? "stderr" : "stdout",
            lines = tail.Lines
        });
    }

    private Task<string> ReadTerminalSnapshotAsync()
    {
        return _terminalSession.CaptureSnapshotAsync(SnapshotOptions);
    }

    private async Task<Task<PromptCompletionResult>> SendPromptCoreAsync(string prompt, bool captureCompletion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        if (_session is null)
        {
            throw new InvalidOperationException("The Copilot session has not been initialized.");
        }

        TaskCompletionSource<PromptCompletionResult>? completion = null;
        if (captureCompletion)
        {
            lock (_promptSyncRoot)
            {
                if (_pendingPromptCompletion is not null)
                {
                    throw new InvalidOperationException("A prompt is already waiting for completion.");
                }

                _assistantMessageCountBeforePrompt = CountAssistantMessages();
                _pendingPromptCompletion = new TaskCompletionSource<PromptCompletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                completion = _pendingPromptCompletion;
            }
        }

        try
        {
            if (string.Equals(_activeTitle, "New session", StringComparison.Ordinal))
            {
                _activeTitle = CreateTitle(prompt);
            }

            var promptStartedAt = DateTimeOffset.UtcNow;
            var promptVersion = Interlocked.Increment(ref _activePromptVersion);
            lock (_promptTimingSyncRoot)
            {
                LatestPromptTiming = new AgentPromptTimingState(
                    promptStartedAt,
                    PersistDuration: null,
                    SnapshotDuration: null,
                    SendDuration: null,
                    TimeToFirstToken: null,
                    TotalResponseDuration: null,
                    ActiveToolDuration: null,
                    LastToolName: null,
                    IsWaitingForFirstToken: true,
                    IsCompleted: false,
                    LastError: null);
            }
            StartFirstTokenTimeout(promptVersion);

            _transcript.AddUserMessage(prompt);
            var persistStartedAt = DateTimeOffset.UtcNow;
            await PersistActiveSessionAsync(cancellationToken);
            UpdatePromptTiming(timing => timing with
            {
                PersistDuration = DateTimeOffset.UtcNow - persistStartedAt
            });

            IsBusy = true;
            StatusText = "Waiting for Copilot...";
            OnStateChanged();

            var sendStartedAt = DateTimeOffset.UtcNow;
            await _session.SendAsync(new MessageOptions
            {
                Prompt = prompt,
                Mode = "immediate"
            });
            UpdatePromptTiming(timing => timing with
            {
                SendDuration = DateTimeOffset.UtcNow - sendStartedAt
            });
        }
        catch (Exception exception)
        {
            CompletePromptTiming(exception.Message);
            CompletePendingPrompt(new PromptCompletionResult(false, [], exception.Message));
            throw;
        }

        return completion?.Task ?? Task.FromResult(new PromptCompletionResult(true, [], null));
    }

    private Task<UserInputResponse> HandleUserInputRequestAsync(UserInputRequest request, UserInputInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invocation);

        var completion = new TaskCompletionSource<UserInputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingUserInputCompletion = completion;
        PendingUserInputRequest = new AgentUserInputRequestState
        {
            Question = request.Question,
            Choices = request.Choices?.Where(choice => !string.IsNullOrWhiteSpace(choice)).ToArray() ?? [],
            AllowFreeformInput = request.AllowFreeform ?? true,
            SelectedChoiceIndex = 0
        };

        StatusText = "Waiting for user input...";
        OnStateChanged();
        return completion.Task;
    }

    private void HandleSessionEvent(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case ToolExecutionStartEvent toolExecutionStartEvent:
                HandleToolExecutionStarted(toolExecutionStartEvent);
                break;

            case ToolExecutionCompleteEvent toolExecutionCompleteEvent:
                HandleToolExecutionCompleted(toolExecutionCompleteEvent);
                break;

            case AssistantMessageDeltaEvent deltaEvent:
                MarkFirstTokenReceived(deltaEvent.Timestamp);
                _transcript.AppendAssistantDelta(deltaEvent.Data.DeltaContent);
                OnStateChanged();
                break;

            case AssistantMessageEvent assistantMessageEvent:
                MarkFirstTokenReceived(assistantMessageEvent.Timestamp);
                _transcript.CompleteAssistantMessage(assistantMessageEvent.Data.Content);
                OnStateChanged();
                break;

            case SessionIdleEvent:
                IsBusy = false;
                StatusText = "Ready";
                CurrentToolActivity = null;
                CompletePromptTiming(errorMessage: null, completedAt: sessionEvent.Timestamp);
                CompletePendingPrompt(new PromptCompletionResult(true, GetAssistantMessagesSincePrompt(), null));
                _ = PersistAndRefreshAsync();
                OnStateChanged();
                break;

            case SessionErrorEvent errorEvent:
                IsBusy = false;
                StatusText = errorEvent.Data.Message;
                CurrentToolActivity = null;
                CompletePromptTiming(errorEvent.Data.Message, errorEvent.Timestamp);
                CompletePendingPrompt(new PromptCompletionResult(false, [], errorEvent.Data.Message));
                _ = PersistAndRefreshAsync();
                OnStateChanged();
                break;
        }
    }

    private void HandleToolExecutionStarted(ToolExecutionStartEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);

        var displayName = string.IsNullOrWhiteSpace(toolEvent.Data.McpServerName)
            ? toolEvent.Data.ToolName
            : $"{toolEvent.Data.McpServerName}/{toolEvent.Data.McpToolName ?? toolEvent.Data.ToolName}";

        CurrentToolActivity = new AgentToolActivityState(
            toolEvent.Data.ToolName,
            displayName,
            BuildToolArgumentsSummary(toolEvent),
            toolEvent.Timestamp,
            CompletedAt: null,
            IsRunning: true,
            Succeeded: false,
            ResultSummary: null);
        UpdatePromptTiming(timing => timing with
        {
            LastToolName = displayName,
            ActiveToolDuration = TimeSpan.Zero
        });
        StatusText = $"Running {displayName}...";
        OnStateChanged();
    }

    private void HandleToolExecutionCompleted(ToolExecutionCompleteEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);

        var activeTool = CurrentToolActivity;
        var completedTool = new AgentToolActivityState(
            activeTool?.ToolName ?? "tool",
            activeTool?.DisplayName ?? activeTool?.ToolName ?? "tool",
            activeTool?.ArgumentsSummary,
            activeTool?.StartedAt ?? toolEvent.Timestamp,
            toolEvent.Timestamp,
            IsRunning: false,
            Succeeded: toolEvent.Data.Success,
            ResultSummary: BuildToolResultSummary(toolEvent));

        CurrentToolActivity = completedTool;
        LastToolActivity = completedTool;
        UpdatePromptTiming(timing => timing with
        {
            LastToolName = completedTool.DisplayName,
            ActiveToolDuration = completedTool.CompletedAt - completedTool.StartedAt
        });
        if (toolEvent.Data.Success)
        {
            StatusText = IsBusy ? "Waiting for Copilot..." : "Ready";
        }
        else
        {
            StatusText = toolEvent.Data.Error?.Message ?? "Tool execution failed.";
        }

        OnStateChanged();
    }

    private static string? BuildToolArgumentsSummary(ToolExecutionStartEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);

        return TrySerializeValue(toolEvent.Data.Arguments);
    }

    private static string? BuildToolResultSummary(ToolExecutionCompleteEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);

        if (!toolEvent.Data.Success)
        {
            return toolEvent.Data.Error?.Message;
        }

        return FirstNonEmpty(
            toolEvent.Data.Result?.Content,
            toolEvent.Data.Result?.DetailedContent,
            TrySerializeValue(toolEvent.Data.Result?.Contents));
    }

    private async Task PersistAndRefreshAsync()
    {
        await PersistActiveSessionAsync();
        SavedSessions = await _conversationSessionStore.ListSessionsAsync();
        OnStateChanged();
    }

    private async Task PersistActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_activeSessionId))
        {
            return;
        }

        var document = new ConversationSessionDocument(
            _activeSessionId,
            _activeTitle,
            _createdAt == default ? DateTimeOffset.UtcNow : _createdAt,
            DateTimeOffset.UtcNow,
            _transcript.Messages.ToArray());

        await _conversationSessionStore.SaveSessionAsync(document, cancellationToken);
    }

    private static string CreateTitle(string prompt)
    {
        const int maxLength = 40;
        var sanitized = prompt.ReplaceLineEndings(" ").Trim();
        if (sanitized.Length <= maxLength)
        {
            return sanitized;
        }

        return string.Concat(sanitized.AsSpan(0, maxLength - 1), "…");
    }

    private static SystemMessageConfig CreateSystemMessage(IReadOnlyList<ConversationMessage> existingMessages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are AgenticTerminal, a terminal-first coding assistant running inside a .NET terminal application with an integrated PowerShell session.");
        builder.AppendLine("Your job is to help the user reason about the current terminal state, propose the next safe step, and use the integrated tools carefully.");
        builder.AppendLine();
        builder.AppendLine("Primary observations available to you:");
        builder.AppendLine("- The current conversation transcript in this session.");
        builder.AppendLine("- The current terminal screen through the read_terminal_snapshot tool.");
        builder.AppendLine("- The output returned by commands you execute through the run_terminal_command tool.");
        builder.AppendLine();
        builder.AppendLine("Important limits on what you can observe:");
        builder.AppendLine("- A terminal snapshot is only a momentary view of the visible terminal screen.");
        builder.AppendLine("- A terminal snapshot is not the full shell history and may omit scrollback or hidden content.");
        builder.AppendLine("- Command output only reflects commands that were executed through your tool flow.");
        builder.AppendLine("- Do not assume terminal state, file contents, or command results that you have not observed.");
        builder.AppendLine();
        builder.AppendLine("Action rules:");
        builder.AppendLine("- Use the run_terminal_command tool when you need to execute shell commands.");
        builder.AppendLine("- The user must approve each terminal command before it runs.");
        builder.AppendLine("- Prefer agt --session=<current-session-id> <exe> [args...] for executable commands that need reliable lifecycle tracking.");
        builder.AppendLine("- Use agt script --session=<current-session-id> -- <script> only when the command cannot be expressed as a direct executable invocation.");
        builder.AppendLine("- Keep shell pipelines shell-owned; wrap individual executable stages with agt only when stage-level tracking is needed.");
        builder.AppendLine("- Use read_wrapped_command_events to inspect wrapped command lifecycle events and recover the hidden commandId values emitted over the backchannel.");
        builder.AppendLine("- Use read_wrapped_command_buffer with a commandId to inspect the per-command stdout or stderr circular buffer when you need recent diagnostic output without flooding the conversation.");
        builder.AppendLine("- The current prompt does not automatically include a terminal snapshot; request one with read_terminal_snapshot when you need it.");
        builder.AppendLine("- Use the read_terminal_snapshot tool when terminal state matters or when the current screen is unclear.");
        builder.AppendLine("- Prefer reading terminal state before suggesting follow-up terminal actions when context is ambiguous.");
        builder.AppendLine("- Be explicit about what you observed versus what you are inferring.");
        builder.AppendLine("- Be cautious, incremental, and terminal-aware rather than acting like a generic chat assistant.");

        if (existingMessages.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Continue this existing conversation using the stored transcript below as context:");
            foreach (var message in existingMessages)
            {
                builder.Append(message.Role);
                builder.Append(": ");
                builder.AppendLine(message.Content.ReplaceLineEndings(" "));
            }
        }

        return new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = builder.ToString()
        };
    }

    private SessionConfig CreateSessionConfig(IReadOnlyList<ConversationMessage> existingMessages, string? preferredModel)
    {
        AIFunction powershellTool = CreateOverridableTool(
            async ([Description("The PowerShell command to execute.")] string command) => await ExecutePowerShellToolAsync(command),
            "powershell",
            "Runs a PowerShell command through the integrated terminal after the user approves it.");

        var sessionConfig = new SessionConfig
        {
            Streaming = true,
            SystemMessage = CreateSystemMessage(existingMessages),
            OnPermissionRequest = PermissionHandler.ApproveAll,
            OnUserInputRequest = HandleUserInputRequestAsync,
            AvailableTools = [.. AllowedToolNames],
            Tools =
            [
                powershellTool,
                AIFunctionFactory.Create(
                    ExecuteTerminalCommandAsync,
                    "run_terminal_command",
                    "Runs a PowerShell command in the integrated terminal after the user approves it."),
                AIFunctionFactory.Create(
                    ReadWrappedCommandEventsAsync,
                    "read_wrapped_command_events",
                    "Returns recent agt wrapper lifecycle events, including commandId values for tracked commands."),
                AIFunctionFactory.Create(
                    ReadWrappedCommandBufferAsync,
                    "read_wrapped_command_buffer",
                    "Returns the tail of the stdout or stderr circular buffer for a tracked agt command."),
                AIFunctionFactory.Create(
                    ReadTerminalSnapshotAsync,
                    "read_terminal_snapshot",
                    "Returns a formatted snapshot of the current integrated terminal screen.")
            ]
        };

        if (!string.IsNullOrWhiteSpace(preferredModel))
        {
            sessionConfig.Model = preferredModel;
        }

        return sessionConfig;
    }

    private static AIFunction CreateOverridableTool(Func<string, Task<object>> handler, string toolName, string description)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var tool = (AIFunction)AIFunctionFactory.Create(handler, toolName, description);
        SetToolAdditionalProperties(tool, ToolOverrideProperties);
        return tool;
    }

    private static void SetToolAdditionalProperties(AITool tool, IReadOnlyDictionary<string, object?> additionalProperties)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(additionalProperties);

        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (var type = tool.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperty("AdditionalProperties", bindingFlags);
            if (property?.SetMethod is not null)
            {
                property.SetValue(tool, additionalProperties);
                return;
            }

            foreach (var field in type.GetFields(bindingFlags))
            {
                if (!string.Equals(field.Name, "<AdditionalProperties>k__BackingField", StringComparison.Ordinal)
                    && !string.Equals(field.Name, "_additionalProperties", StringComparison.Ordinal)
                    && !string.Equals(field.Name, "AdditionalProperties", StringComparison.Ordinal)
                    && !IsAdditionalPropertiesFieldType(field.FieldType))
                {
                    continue;
                }

                field.SetValue(tool, additionalProperties);
                return;
            }
        }

        throw new InvalidOperationException("The AI tool override metadata field could not be found.");
    }

    private static bool IsAdditionalPropertiesFieldType(Type fieldType)
    {
        return typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(fieldType)
            || typeof(IDictionary<string, object?>).IsAssignableFrom(fieldType);
    }

    private async Task ReplaceAgentCommandBackchannelSessionAsync()
    {
        if (_agentCommandBackchannelSession is not null)
        {
            await _agentCommandBackchannelSession.DisposeAsync();
            _agentCommandBackchannelSession = null;
        }

        if (!string.IsNullOrWhiteSpace(_activeSessionId))
        {
            _agentCommandBackchannelSession = new AgentCommandBackchannelSession(_activeSessionId);
        }
    }

    private async Task RefreshModelStateAsync(CancellationToken cancellationToken)
    {
        if (_client is null || _session is null)
        {
            AvailableModels = [];
            ActiveModelId = _preferredModel;
            RemainingQuotaPercentage = null;
            return;
        }

        var models = await _client.ListModelsAsync(cancellationToken);
        AvailableModels = models
            .Where(model => !string.Equals(model.Policy?.State, "disabled", StringComparison.OrdinalIgnoreCase))
            .OrderBy(model => string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var currentModel = await _session.Rpc.Model.GetCurrentAsync(cancellationToken);
        ActiveModelId = currentModel.ModelId;
        if (!string.IsNullOrWhiteSpace(ActiveModelId))
        {
            _preferredModel = ActiveModelId;
        }

        var quota = await _client.Rpc.Account.GetQuotaAsync(gitHubToken: null, cancellationToken: cancellationToken);
        RemainingQuotaPercentage = ResolveRemainingQuotaPercentage(quota);
    }

    private static double? ResolveRemainingQuotaPercentage(AccountGetQuotaResult quota)
    {
        ArgumentNullException.ThrowIfNull(quota);

        if (TryGetRemainingPercentage(quota, "premium_interactions", out var remainingPercentage)
            || TryGetRemainingPercentage(quota, "chat", out remainingPercentage)
            || TryGetRemainingPercentage(quota, "completions", out remainingPercentage))
        {
            return remainingPercentage;
        }

        return quota.QuotaSnapshots
            .Select(snapshot => (double?)snapshot.Value.RemainingPercentage)
            .FirstOrDefault();
    }

    private static bool TryGetRemainingPercentage(AccountGetQuotaResult quota, string quotaName, out double remainingPercentage)
    {
        if (quota.QuotaSnapshots.TryGetValue(quotaName, out var snapshot))
        {
            remainingPercentage = snapshot.RemainingPercentage;
            return true;
        }

        remainingPercentage = default;
        return false;
    }

    private ModelInfo? ResolveActiveModel()
    {
        if (string.IsNullOrWhiteSpace(ActiveModelId))
        {
            return null;
        }

        return AvailableModels.FirstOrDefault(model => string.Equals(model.Id, ActiveModelId, StringComparison.Ordinal));
    }

    private string GetActiveModelDisplayName()
    {
        var activeModel = ResolveActiveModel();
        return string.IsNullOrWhiteSpace(activeModel?.Name)
            ? ActiveModelId ?? "Copilot default"
            : activeModel.Name;
    }

    private int CountAssistantMessages()
    {
        return _transcript.Messages.Count(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal));
    }

    private IReadOnlyList<string> GetAssistantMessagesSincePrompt()
    {
        var assistantMessages = _transcript.Messages
            .Where(message => string.Equals(message.Role, "assistant", StringComparison.Ordinal))
            .Skip(_assistantMessageCountBeforePrompt)
            .Select(message => message.Content)
            .ToArray();

        return assistantMessages;
    }

    private void CompletePendingPrompt(PromptCompletionResult result)
    {
        TaskCompletionSource<PromptCompletionResult>? completion;
        lock (_promptSyncRoot)
        {
            completion = _pendingPromptCompletion;
            _pendingPromptCompletion = null;
            _assistantMessageCountBeforePrompt = 0;
        }

        completion?.TrySetResult(result);
    }

    private void MarkFirstTokenReceived(DateTimeOffset tokenReceivedAt)
    {
        CancelActivePromptTimeout();
        UpdatePromptTiming(timing => timing.TimeToFirstToken is not null
            ? timing
            : timing with
            {
                TimeToFirstToken = tokenReceivedAt - timing.StartedAt,
                IsWaitingForFirstToken = false
            });
    }

    private void CompletePromptTiming(string? errorMessage, DateTimeOffset? completedAt = null)
    {
        CancelActivePromptTimeout();
        UpdatePromptTiming(timing => timing with
        {
            IsWaitingForFirstToken = false,
            IsCompleted = true,
            TotalResponseDuration = (completedAt ?? DateTimeOffset.UtcNow) - timing.StartedAt,
            LastError = errorMessage
        });
    }

    private void UpdatePromptTiming(Func<AgentPromptTimingState, AgentPromptTimingState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        lock (_promptTimingSyncRoot)
        {
            var timing = LatestPromptTiming;
            if (timing is null)
            {
                return;
            }

            LatestPromptTiming = updater(timing);
        }
    }

    private void ClearPendingUserInputRequest()
    {
        _pendingUserInputCompletion = null;
        PendingUserInputRequest = null;
    }

    private void StartFirstTokenTimeout(long promptVersion)
    {
        CancellationTokenSource? previousCancellation;
        var cancellation = new CancellationTokenSource();
        lock (_promptTimingSyncRoot)
        {
            previousCancellation = _activePromptTimeoutCancellation;
            _activePromptTimeoutCancellation = cancellation;
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        _ = MonitorFirstTokenTimeoutAsync(promptVersion, cancellation.Token);
    }

    private void CancelActivePromptTimeout()
    {
        CancellationTokenSource? cancellation;
        lock (_promptTimingSyncRoot)
        {
            cancellation = _activePromptTimeoutCancellation;
            _activePromptTimeoutCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task MonitorFirstTokenTimeoutAsync(long promptVersion, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_firstTokenTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!TryTimeoutPrompt(promptVersion, out var timeoutMessage))
        {
            return;
        }

        try
        {
            if (_session is not null)
            {
                await _session.AbortAsync();
            }
        }
        catch
        {
        }

        IsBusy = false;
        CurrentToolActivity = null;
        StatusText = timeoutMessage;
        CompletePendingPrompt(new PromptCompletionResult(false, [], timeoutMessage));
        OnStateChanged();
    }

    private bool TryTimeoutPrompt(long promptVersion, out string timeoutMessage)
    {
        timeoutMessage = $"Timed out waiting for first token after {_firstTokenTimeout.TotalSeconds:0} s.";

        lock (_promptTimingSyncRoot)
        {
            if (promptVersion != _activePromptVersion || LatestPromptTiming is null || LatestPromptTiming.IsCompleted || !LatestPromptTiming.IsWaitingForFirstToken)
            {
                return false;
            }

            LatestPromptTiming = LatestPromptTiming with
            {
                IsWaitingForFirstToken = false,
                IsCompleted = true,
                TotalResponseDuration = DateTimeOffset.UtcNow - LatestPromptTiming.StartedAt,
                LastError = timeoutMessage
            };
        }

        CancelActivePromptTimeout();
        return true;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? TrySerializeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            var serialized = JsonSerializer.Serialize(value);
            return serialized.Length <= 200 ? serialized : string.Concat(serialized.AsSpan(0, 197), "...");
        }
        catch
        {
            var fallback = value.ToString();
            if (string.IsNullOrWhiteSpace(fallback))
            {
                return null;
            }

            return fallback.Length <= 200 ? fallback : string.Concat(fallback.AsSpan(0, 197), "...");
        }
    }

    private void HandleStateChanged()
    {
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke();
    }
}
