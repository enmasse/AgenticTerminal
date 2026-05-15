using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using Avalonia.Threading;
using GitHub.Copilot.SDK;
using ConversationMessage = AgenticTerminal.Persistence.ConversationMessage;

namespace AgenticTerminal.UI;

/// <summary>
/// Shared MVVM ViewModel for the agent panel. Both the Hex1b embedded panel and the Avalonia
/// popup panel read from this. The Avalonia view binds to it via {Binding}; the Hex1b panel
/// reads properties directly during rendering.
/// </summary>
public sealed class AgentPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CopilotAgentSessionManager _sessionManager;

    private string _statusText = string.Empty;
    private bool _isBusy;
    private ApprovalRequest? _pendingApproval;
    private AgentToolActivityState? _currentToolActivity;
    private string? _activeModelId;
    private double? _remainingQuotaPercentage;
    private string _promptText = string.Empty;
    private AgentUserInputRequestState? _pendingUserInputRequest;

    public AgentPanelViewModel(CopilotAgentSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        InitCommands();
        _sessionManager.StateChanged += OnSessionManagerStateChanged;
        SyncFromSessionManager();
    }

    private void InitCommands()
    {
        SendPromptCommand = new RelayCommand(
            execute: async _ => await SendPromptAsync(),
            canExecute: _ => !string.IsNullOrWhiteSpace(_promptText)
                             && (!_isBusy || _pendingUserInputRequest is not null));

        ApproveCommand = new RelayCommand(
            execute: async _ => await ResolveApprovalAsync(approved: true),
            canExecute: _ => _pendingApproval is not null);

        DenyCommand = new RelayCommand(
            execute: async _ => await ResolveApprovalAsync(approved: false),
            canExecute: _ => _pendingApproval is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ─── Observable properties ─────────────────────────────────────────────────

    /// <summary>Conversation messages wrapped for Avalonia binding.</summary>
    public ObservableCollection<ConversationMessageViewModel> MessageViewModels { get; } = [];

    /// <summary>Conversation messages in order.</summary>
    public ObservableCollection<ConversationMessage> Messages { get; } = [];

    /// <summary>Saved sessions list.</summary>
    public ObservableCollection<ConversationSessionSummary> SavedSessions { get; } = [];

    /// <summary>Available Copilot models.</summary>
    public ObservableCollection<ModelInfo> AvailableModels { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                SendPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ApprovalRequest? PendingApproval
    {
        get => _pendingApproval;
        private set
        {
            if (Set(ref _pendingApproval, value))
            {
                ApproveCommand.RaiseCanExecuteChanged();
                DenyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AgentUserInputRequestState? PendingUserInputRequest
    {
        get => _pendingUserInputRequest;
        private set
        {
            if (Set(ref _pendingUserInputRequest, value))
            {
                SendPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AgentToolActivityState? CurrentToolActivity
    {
        get => _currentToolActivity;
        private set => Set(ref _currentToolActivity, value);
    }

    public string? ActiveModelId
    {
        get => _activeModelId;
        private set => Set(ref _activeModelId, value);
    }

    public double? RemainingQuotaPercentage
    {
        get => _remainingQuotaPercentage;
        private set => Set(ref _remainingQuotaPercentage, value);
    }

    public string PromptText
    {
        get => _promptText;
        set
        {
            if (Set(ref _promptText, value))
            {
                SendPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ─── Commands ──────────────────────────────────────────────────────────────

    public RelayCommand SendPromptCommand { get; private set; } = null!;
    public RelayCommand ApproveCommand { get; private set; } = null!;
    public RelayCommand DenyCommand { get; private set; } = null!;

    // ─── Pass-through access for Hex1b panel ──────────────────────────────────

    public CopilotAgentSessionManager SessionManager => _sessionManager;

    // ─── Actions ──────────────────────────────────────────────────────────────

    public async Task SendPromptAsync()
    {
        var prompt = _promptText.TrimEnd();
        if (string.IsNullOrWhiteSpace(prompt)) return;
        PromptText = string.Empty;

        // If the agent is waiting for user input, submit the answer via the
        // dedicated path instead of starting a new prompt turn.
        if (_pendingUserInputRequest is not null)
        {
            await _sessionManager.SubmitUserInputAsync(prompt);
            return;
        }

        await _sessionManager.SendPromptAsync(prompt);
    }

    public async Task ResolveApprovalAsync(bool approved)
    {
        if (_sessionManager.PendingApproval is null) return;
        await _sessionManager.ResolvePendingApprovalAsync(approved);
    }

    // ─── State sync ───────────────────────────────────────────────────────────

    private void OnSessionManagerStateChanged()
    {
        // StateChanged is raised from the Hex1b/agent background thread.
        // All property setters raise PropertyChanged and CanExecuteChanged,
        // which Avalonia requires to be called from the UI thread.
        Dispatcher.UIThread.Post(SyncFromSessionManager);
    }

    private void SyncFromSessionManager()
    {
        StatusText = _sessionManager.StatusText;
        IsBusy = _sessionManager.IsBusy;
        PendingApproval = _sessionManager.PendingApproval;
        PendingUserInputRequest = _sessionManager.PendingUserInputRequest;
        CurrentToolActivity = _sessionManager.CurrentToolActivity;
        ActiveModelId = _sessionManager.ActiveModelId;
        RemainingQuotaPercentage = _sessionManager.RemainingQuotaPercentage;

        SyncCollection(Messages, _sessionManager.Messages);
        SyncMessageViewModels(_sessionManager.Messages);
        SyncCollection(SavedSessions, _sessionManager.SavedSessions);
        SyncCollection(AvailableModels, _sessionManager.AvailableModels);
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        // Minimal diff: only update if content differs
        if (target.Count == source.Count)
        {
            bool same = true;
            for (int i = 0; i < source.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], source[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
            {
                return;
            }
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void SyncMessageViewModels(IReadOnlyList<ConversationMessage> source)
    {
        if (MessageViewModels.Count == source.Count)
        {
            return;
        }

        // Only append new items to preserve scroll position
        var startIndex = MessageViewModels.Count;
        for (int i = startIndex; i < source.Count; i++)
        {
            MessageViewModels.Add(new ConversationMessageViewModel(source[i]));
        }

        // If source shrank (e.g. new session), rebuild
        if (MessageViewModels.Count > source.Count)
        {
            MessageViewModels.Clear();
            foreach (var msg in source)
            {
                MessageViewModels.Add(new ConversationMessageViewModel(msg));
            }
        }
    }

    // ─── INotifyPropertyChanged ───────────────────────────────────────────────

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_sessionManager is not null)
        {
            _sessionManager.StateChanged -= OnSessionManagerStateChanged;
        }
    }
}

/// <summary>Simple ICommand implementation that supports async execute and CanExecute refresh.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            // Continuations after await may run on a threadpool thread.
            // Avalonia requires CanExecuteChanged to be raised on the UI thread.
            Avalonia.Threading.Dispatcher.UIThread.Post(RaiseCanExecuteChanged);
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
