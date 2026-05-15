using AgenticTerminal.Agent;
using AgenticTerminal.Persistence;
using Hex1b;
using Hex1b.Events;
using Hex1b.Input;
using Hex1b.Layout;

namespace AgenticTerminal.UI;

/// <summary>
/// Agent panel rendered as an inline right-hand pane inside the Hex1b TUI.
/// </summary>
internal sealed class Hex1bEmbeddedAgentPanel : IHex1bEmbeddedAgentPanel
{
    private const string ApprovalMetricName = "approval-prompt";
    private const string UserInputMetricName = "user-input";
    private const string SessionsMetricName = "sessions-list";
    private const string PromptMetricName = "agent-prompt";
    private const string ToolActivityMetricName = "tool-activity";
    private const string ModelDialogListMetricName = "model-dialog-list";

    private readonly AgentPanelViewModel _viewModel;
    private CopilotAgentSessionManager SessionManager => _viewModel.SessionManager;

    // Agent-specific UI state
    private string _promptText = string.Empty;
    private string _userInputText = string.Empty;
    private int _selectedSessionIndex;
    private int _selectedModelIndex;
    private int _selectedUserChoiceIndex;
    private bool _isUserInputActive;
    private bool _isModelDialogOpen;
    private Hex1bFocusTarget _focusTarget = Hex1bFocusTarget.Prompt;

    // Hex1b app-level callbacks injected by the shell
    private Action<Func<Hex1bNode, bool>>? _requestFocus;
    private Action? _invalidate;

    // Model dialog window handle (owned by this panel)
    private WindowHandle? _modelDialogWindow;
    private Action? _requestModelDialogFocus;

    public bool IsEmbedded => true;

    public bool HasPendingApproval => _viewModel.PendingApproval is not null;

    public Hex1bEmbeddedAgentPanel(AgentPanelViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    // ─── IHex1bEmbeddedAgentPanel ──────────────────────────────────────────────

    public void Attach(Action<Func<Hex1bNode, bool>> requestFocus, Action invalidate)
    {
        _requestFocus = requestFocus;
        _invalidate = invalidate;
    }

    public void Detach()
    {
        _requestFocus = null;
        _invalidate = null;
        _requestModelDialogFocus = null;
    }

    public void Subscribe(Action onStateChanged)
    {
        SessionManager.StateChanged += onStateChanged;
    }

    public void Unsubscribe(Action onStateChanged)
    {
        SessionManager.StateChanged -= onStateChanged;
    }

    public void ConfigureApprovalBindings(InputBindingsBuilder bindings)
    {
        Hex1bShellInputBindings.ConfigureApprovalBindings(
            bindings,
            () => ResolveApprovalAsync(approved: true),
            () => ResolveApprovalAsync(approved: false));
    }

    public void OpenModelDialog(WindowManager windows)
    {
        _modelDialogWindow ??= windows.Window(w => BuildModelDialog(w))
            .Title("Change model")
            .Size(76, 18)
            .Modal()
            .OnClose(() => _isModelDialogOpen = false)
            .OnActivated(() => _requestModelDialogFocus?.Invoke());

        if (_modelDialogWindow is null)
        {
            return;
        }

        SyncSelectedModelIndex();
        _isModelDialogOpen = true;
        windows.Open(_modelDialogWindow);
        _requestModelDialogFocus?.Invoke();
    }

    public Hex1b.Widgets.Hex1bWidget BuildWidget<TParent>(Hex1b.WidgetContext<TParent> context)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        var sessionItems = _viewModel.SavedSessions
            .Select(AgentShellTextFormatter.FormatSession)
            .ToArray();
        var hasPendingApproval = _viewModel.PendingApproval is not null;
        var pendingUserInput = SessionManager.PendingUserInputRequest;
        var hasPendingUserInput = pendingUserInput is not null;
        var approvalPrompt = AgentShellTextFormatter.BuildApprovalPrompt(_viewModel.PendingApproval);

        Hex1b.Widgets.Hex1bWidget approvalWidget = hasPendingApproval
            ? new Hex1b.Widgets.BorderWidget(
                ((new Hex1b.Widgets.InteractableWidget(_ =>
                    new Hex1b.Widgets.TextBlockWidget(approvalPrompt)
                    {
                        HeightHint = SizeHint.Content,
                        WidthHint = SizeHint.Fill
                    })
                {
                    MetricName = ApprovalMetricName,
                    HeightHint = SizeHint.Content,
                    WidthHint = SizeHint.Fill
                })
                .OnFocusChanged(args =>
                {
                    if (args.IsFocused)
                    {
                        _focusTarget = Hex1bFocusTarget.Approval;
                    }
                })
                .InputBindings(bindings =>
                {
                    Hex1bShellInputBindings.ConfigureApprovalBindings(
                        bindings,
                        () => ResolveApprovalAsync(approved: true),
                        () => ResolveApprovalAsync(approved: false));
                })))
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }.Title("Approval")
            : new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content };

        var userInputWidget = BuildUserInputPanel(context);

        Hex1b.Widgets.Hex1bWidget promptEditor = hasPendingApproval || hasPendingUserInput
            ? new Hex1b.Widgets.TextBlockWidget(string.IsNullOrEmpty(_promptText) ? string.Empty : _promptText)
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }
            : new Hex1b.Widgets.TextBoxWidget(_promptText)
            {
                MetricName = PromptMetricName,
                MinWidth = 20,
                WidthHint = SizeHint.Fill
            }
            .Multiline()
            .Height(4)
            .OnTextChanged(args =>
            {
                _focusTarget = Hex1bFocusTarget.Prompt;
                _promptText = args.NewText;
            })
            .InputBindings(bindings =>
            {
                Hex1bShellInputBindings.ConfigurePromptBindings(bindings, () => SendPromptAsync(_promptText));
            });

        var sessionList = new Hex1b.Widgets.ListWidget(sessionItems)
        {
            InitialSelectedIndex = Math.Clamp(_selectedSessionIndex, 0, Math.Max(0, sessionItems.Length - 1)),
            HeightHint = SizeHint.Content,
            MetricName = SessionsMetricName
        }
        .OnSelectionChanged(HandleSessionSelectionChanged)
        .OnItemActivated(async args => await OpenSessionAsync(args.ActivatedIndex));

        Hex1b.Widgets.Hex1bWidget conversationHistory = new Hex1b.Widgets.ScrollPanelWidget(
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.FormatConversation(_viewModel.Messages.ToList()))
            {
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill
            },
            Hex1b.Widgets.ScrollOrientation.Vertical,
            true)
        {
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill
        }
        .Follow();

        var promptHelpText = hasPendingApproval
            ? "Prompt: locked while approval is waiting"
            : hasPendingUserInput
                ? "Prompt: locked while the agent waits for your answer"
                : "Enter = send · Shift-Enter = new line";

        return new Hex1b.Widgets.BorderWidget(
            context.VStack(_ =>
            [
                new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildSessionSummary()) { HeightHint = SizeHint.Content },
                sessionList,
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                conversationHistory,
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                BuildToolActivityPanel(),
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                approvalWidget,
                userInputWidget,
                new Hex1b.Widgets.BorderWidget(promptEditor)
                {
                    HeightHint = SizeHint.Content,
                    WidthHint = SizeHint.Fill
                }.Title("Prompt"),
                new Hex1b.Widgets.TextBlockWidget(promptHelpText) { HeightHint = SizeHint.Content },
                new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildStatusText(SessionManager, _focusTarget)) { HeightHint = SizeHint.Content }
            ]))
        {
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill
        }.Title("Agent");
    }

    public Hex1b.Widgets.Hex1bWidget BuildModelDialog(Hex1b.WindowContentContext<Hex1b.Widgets.Hex1bWidget> context)
    {
        _requestModelDialogFocus = () =>
        {
            _requestFocus?.Invoke(node => string.Equals(node.MetricName, ModelDialogListMetricName, StringComparison.Ordinal));
            _invalidate?.Invoke();
        };

        var models = _viewModel.AvailableModels;
        var modelItems = models
            .Select(model => AgentShellTextFormatter.FormatModelOption(
                model,
                string.Equals(model.Id, _viewModel.ActiveModelId, StringComparison.Ordinal)))
            .ToArray();

        if (modelItems.Length == 0)
        {
            return context.VStack(_ =>
            [
                new Hex1b.Widgets.TextBlockWidget("No models are available for the current Copilot session.") { HeightHint = SizeHint.Content },
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                _.HStack(h =>
                [
                    h.Button("Close").OnClick(_ => context.Window.Cancel())
                ])
            ]);
        }

        return context.VStack(_ =>
        [
            new Hex1b.Widgets.TextBlockWidget("Select a Copilot model for this session.") { HeightHint = SizeHint.Content },
            new Hex1b.Widgets.TextBlockWidget("The active model is marked with ●.") { HeightHint = SizeHint.Content },
            new Hex1b.Widgets.TextBlockWidget("Enter applies the selected model. Esc closes this dialog.") { HeightHint = SizeHint.Content },
            new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
            new Hex1b.Widgets.ListWidget(modelItems)
            {
                InitialSelectedIndex = Math.Clamp(_selectedModelIndex, 0, Math.Max(0, modelItems.Length - 1)),
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill,
                MetricName = ModelDialogListMetricName
            }
            .OnSelectionChanged(args => _selectedModelIndex = args.SelectedIndex)
            .OnItemActivated(async args => await ChangeModelFromDialogAsync(context.Window, args.ActivatedIndex, args.CancellationToken))
            .InputBindings(bindings =>
            {
                Hex1bShellInputBindings.ConfigureModelDialogBindings(
                    bindings,
                    () => context.Window.Cancel());
            })
        ]);
    }

    // ─── IAgentPanel ──────────────────────────────────────────────────────────

    public Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void NotifyStateChanged()
    {
        _isUserInputActive = SessionManager.PendingUserInputRequest is not null;

        if (_viewModel.PendingApproval is not null)
        {
            _focusTarget = Hex1bFocusTarget.Approval;
            _requestFocus?.Invoke(node => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal));
            return;
        }

        if (SessionManager.PendingUserInputRequest is { } userInputRequest)
        {
            _isUserInputActive = true;
            _selectedUserChoiceIndex = Math.Clamp(_selectedUserChoiceIndex, 0, Math.Max(0, userInputRequest.Choices.Count - 1));
            if (string.IsNullOrEmpty(_userInputText))
            {
                _userInputText = userInputRequest.PromptText;
            }

            _focusTarget = Hex1bFocusTarget.UserInput;
            _requestFocus?.Invoke(node => string.Equals(node.MetricName, UserInputMetricName, StringComparison.Ordinal));
            return;
        }

        _isUserInputActive = false;
        _userInputText = string.Empty;
        _selectedUserChoiceIndex = 0;

        if (Hex1bApplicationShell.ShouldRestorePromptFocusAfterAgentUpdate(_focusTarget))
        {
            _focusTarget = Hex1bFocusTarget.Prompt;
            _requestFocus?.Invoke(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─── Private widget builders ──────────────────────────────────────────────

    private Hex1b.Widgets.Hex1bWidget BuildToolActivityPanel()
    {
        return new Hex1b.Widgets.BorderWidget(
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildToolActivityText(SessionManager))
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill,
                MetricName = ToolActivityMetricName
            })
        {
            HeightHint = SizeHint.Content,
            WidthHint = SizeHint.Fill
        }.Title("Tool activity");
    }

    private Hex1b.Widgets.Hex1bWidget BuildUserInputPanel<TParent>(Hex1b.WidgetContext<TParent> context)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        var request = SessionManager.PendingUserInputRequest;
        if (request is null)
        {
            return new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content };
        }

        _selectedUserChoiceIndex = Math.Clamp(_selectedUserChoiceIndex, 0, Math.Max(0, request.Choices.Count - 1));

        var children = new List<Hex1b.Widgets.Hex1bWidget>
        {
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildUserInputPrompt(request))
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }
        };

        if (request.HasChoices)
        {
            children.Add(new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content });
            children.Add(new Hex1b.Widgets.ListWidget(request.Choices.ToArray())
            {
                InitialSelectedIndex = _selectedUserChoiceIndex,
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill,
                MetricName = request.AllowFreeformInput ? null : UserInputMetricName
            }
            .OnSelectionChanged(args =>
            {
                _focusTarget = Hex1bFocusTarget.UserInput;
                _selectedUserChoiceIndex = args.SelectedIndex;
            })
            .OnItemActivated(async args => await SubmitPendingUserChoiceAsync(args.ActivatedIndex)));
        }

        if (request.AllowFreeformInput)
        {
            children.Add(new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content });
            children.Add(new Hex1b.Widgets.BorderWidget(
                new Hex1b.Widgets.TextBoxWidget(_userInputText)
                {
                    MetricName = UserInputMetricName,
                    MinWidth = 20,
                    WidthHint = SizeHint.Fill
                }
                .Multiline()
                .Height(4)
                .OnTextChanged(args =>
                {
                    _focusTarget = Hex1bFocusTarget.UserInput;
                    _userInputText = args.NewText;
                })
                .InputBindings(bindings =>
                {
                    Hex1bShellInputBindings.ConfigureUserInputBindings(
                        bindings,
                        allowMultiline: true,
                        () => SubmitPendingUserInputAsync(),
                        () => CancelPendingUserInputAsync());
                }))
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }.Title("Response"));
        }

        children.Add(new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildUserInputHelpText(request))
        {
            HeightHint = SizeHint.Content,
            WidthHint = SizeHint.Fill
        });

        return new Hex1b.Widgets.BorderWidget(context.VStack(_ => [.. children]))
        {
            HeightHint = SizeHint.Content,
            WidthHint = SizeHint.Fill
        }.Title("Question");
    }

    // ─── Session / focus helpers ──────────────────────────────────────────────

    public void FocusSessionsPane(InputBindingActionContext context)
    {
        _focusTarget = Hex1bFocusTarget.Sessions;
        context.FocusWhere(node => string.Equals(node.MetricName, SessionsMetricName, StringComparison.Ordinal));
    }

    public void FocusPromptPane(InputBindingActionContext context)
    {
        if (_viewModel.PendingApproval is not null)
        {
            _focusTarget = Hex1bFocusTarget.Approval;
            context.FocusWhere(node => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal));
        }
        else if (SessionManager.PendingUserInputRequest is not null)
        {
            _focusTarget = Hex1bFocusTarget.UserInput;
            context.FocusWhere(node => string.Equals(node.MetricName, UserInputMetricName, StringComparison.Ordinal));
        }
        else
        {
            _focusTarget = Hex1bFocusTarget.Prompt;
            context.FocusWhere(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal));
        }
    }

    private void HandleSessionSelectionChanged(ListSelectionChangedEventArgs args)
    {
        _focusTarget = Hex1bFocusTarget.Sessions;
        _selectedSessionIndex = args.SelectedIndex;
    }

    // ─── Agent actions ────────────────────────────────────────────────────────

    private async Task SendPromptAsync(string prompt)
    {
        var normalizedPrompt = prompt.TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return;
        }

        _promptText = string.Empty;
        await SessionManager.SendPromptAsync(normalizedPrompt);
    }

    private async Task SubmitPendingUserInputAsync()
    {
        var normalizedInput = _userInputText.TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return;
        }

        var submitted = await SessionManager.SubmitUserInputAsync(normalizedInput);
        if (submitted)
        {
            _userInputText = string.Empty;
        }
    }

    private async Task SubmitPendingUserChoiceAsync(int selectedIndex)
    {
        var submitted = await SessionManager.SubmitUserChoiceAsync(selectedIndex);
        if (submitted)
        {
            _selectedUserChoiceIndex = selectedIndex;
            _userInputText = string.Empty;
        }
    }

    private async Task CancelPendingUserInputAsync()
    {
        var canceled = await SessionManager.CancelUserInputRequestAsync();
        if (canceled)
        {
            _userInputText = string.Empty;
        }
    }

    private async Task OpenSessionAsync(int activatedIndex)
    {
        if (activatedIndex < 0 || activatedIndex >= _viewModel.SavedSessions.Count)
        {
            return;
        }

        _selectedSessionIndex = activatedIndex;
        await SessionManager.OpenStoredSessionAsync(_viewModel.SavedSessions[activatedIndex].SessionId);
    }

    private async Task ResolveApprovalAsync(bool approved)
    {
        if (_viewModel.PendingApproval is null)
        {
            return;
        }

        await SessionManager.ResolvePendingApprovalAsync(approved);
    }

    private async Task ChangeModelFromDialogAsync(WindowHandle dialogWindow, int selectedIndex, CancellationToken cancellationToken)
    {
        var models = _viewModel.AvailableModels;
        if (selectedIndex < 0 || selectedIndex >= models.Count)
        {
            return;
        }

        _selectedModelIndex = selectedIndex;
        var changed = await SessionManager.ChangeModelAsync(models[selectedIndex].Id, cancellationToken);
        if (changed)
        {
            dialogWindow.Cancel();
        }
    }

    private void SyncSelectedModelIndex()
    {
        var models = _viewModel.AvailableModels;
        if (models.Count == 0)
        {
            _selectedModelIndex = 0;
            return;
        }

        var activeModelIndex = models
            .Select((model, index) => new { model.Id, index })
            .FirstOrDefault(item => string.Equals(item.Id, _viewModel.ActiveModelId, StringComparison.Ordinal))?.index;

        _selectedModelIndex = activeModelIndex ?? Math.Clamp(_selectedModelIndex, 0, models.Count - 1);
    }
}
