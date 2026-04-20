using System.ComponentModel;
using System.Text;
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

    private readonly ApprovalQueue _approvalQueue;
    private readonly ConversationSessionStore _conversationSessionStore;
    private readonly ConversationTranscript _transcript = new();
    private readonly ITerminalSession _terminalSession;
    private readonly string _workingDirectory;
    private readonly object _promptSyncRoot = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private CopilotClient? _client;
    private CopilotSession? _session;
    private IDisposable? _sessionSubscription;
    private TaskCompletionSource<PromptCompletionResult>? _pendingPromptCompletion;
    private int _assistantMessageCountBeforePrompt;
    private DateTimeOffset _createdAt;
    private string? _activeSessionId;
    private string _activeTitle = "New session";
    private string? _preferredModel;

    public CopilotAgentSessionManager(
        ApprovalQueue approvalQueue,
        ConversationSessionStore conversationSessionStore,
        ITerminalSession terminalSession,
        string workingDirectory,
        string? preferredModel = null)
    {
        _approvalQueue = approvalQueue;
        _conversationSessionStore = conversationSessionStore;
        _terminalSession = terminalSession;
        _workingDirectory = workingDirectory;
        _preferredModel = string.IsNullOrWhiteSpace(preferredModel) ? null : preferredModel;
        _approvalQueue.Changed += HandleStateChanged;
    }

    public event Action? StateChanged;

    public IReadOnlyList<ConversationMessage> Messages => _transcript.Messages;

    public IReadOnlyList<ConversationSessionSummary> SavedSessions { get; private set; } = [];

    public IReadOnlyList<ModelInfo> AvailableModels { get; private set; } = [];

    public ApprovalRequest? PendingApproval => _approvalQueue.Current;

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

    public async ValueTask DisposeAsync()
    {
        _approvalQueue.Changed -= HandleStateChanged;
        _sessionSubscription?.Dispose();
        CompletePendingPrompt(new PromptCompletionResult(false, [], "The Copilot session was disposed before the prompt completed."));
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        if (_client is not null)
        {
            await _client.StopAsync();
            await _client.DisposeAsync();
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

        var result = await _terminalSession.ExecuteCommandAsync(command);
        return new
        {
            approved = true,
            exitCode = result.ExitCode,
            output = result.Output
        };
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

            _transcript.AddUserMessage(prompt);
            await PersistActiveSessionAsync(cancellationToken);

            IsBusy = true;
            StatusText = "Waiting for Copilot...";
            OnStateChanged();

            var terminalSnapshot = await _terminalSession.CaptureSnapshotAsync(SnapshotOptions, cancellationToken);
            var composedPrompt = AgentPromptComposer.Compose(prompt, terminalSnapshot);

            await _session.SendAsync(new MessageOptions
            {
                Prompt = composedPrompt,
                Mode = "immediate"
            });
        }
        catch (Exception exception)
        {
            CompletePendingPrompt(new PromptCompletionResult(false, [], exception.Message));
            throw;
        }

        return completion?.Task ?? Task.FromResult(new PromptCompletionResult(true, [], null));
    }

    private void HandleSessionEvent(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case AssistantMessageDeltaEvent deltaEvent:
                _transcript.AppendAssistantDelta(deltaEvent.Data.DeltaContent);
                OnStateChanged();
                break;

            case AssistantMessageEvent assistantMessageEvent:
                _transcript.CompleteAssistantMessage(assistantMessageEvent.Data.Content);
                OnStateChanged();
                break;

            case SessionIdleEvent:
                IsBusy = false;
                StatusText = "Ready";
                CompletePendingPrompt(new PromptCompletionResult(true, GetAssistantMessagesSincePrompt(), null));
                _ = PersistAndRefreshAsync();
                OnStateChanged();
                break;

            case SessionErrorEvent errorEvent:
                IsBusy = false;
                StatusText = errorEvent.Data.Message;
                CompletePendingPrompt(new PromptCompletionResult(false, [], errorEvent.Data.Message));
                _ = PersistAndRefreshAsync();
                OnStateChanged();
                break;
        }
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
        var sessionConfig = new SessionConfig
        {
            Streaming = true,
            SystemMessage = CreateSystemMessage(existingMessages),
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Tools =
            [
                AIFunctionFactory.Create(
                    ExecuteTerminalCommandAsync,
                    "run_terminal_command",
                    "Runs a PowerShell command in the integrated terminal after the user approves it."),
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

        var quota = await _client.Rpc.Account.GetQuotaAsync(cancellationToken);
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

    private void HandleStateChanged()
    {
        OnStateChanged();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke();
    }
}
