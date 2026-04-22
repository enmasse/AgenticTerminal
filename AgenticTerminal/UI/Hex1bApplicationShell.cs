using AgenticTerminal.Agent;
using AgenticTerminal.Terminal;
using Hex1b;
using Hex1b.Events;
using Hex1b.Input;
using Hex1b.Layout;

namespace AgenticTerminal.UI;

public sealed class Hex1bApplicationShell : IApplicationShell
{
    private const int MinimumTerminalPaneWidth = 40;
    private const int MaximumTerminalPaneWidth = 160;
    private const string TerminalMetricName = "terminal-pane";
    private const string ApprovalMetricName = "approval-prompt";
    private const string UserInputMetricName = "user-input";
    private const string SessionsMetricName = "sessions-list";
    private const string PromptMetricName = "agent-prompt";
    private const string ToolActivityMetricName = "tool-activity";
    private const string DebugPanelMetricName = "debug-panel";
    private const string ModelDialogListMetricName = "model-dialog-list";

    internal static bool MatchesApprovalInput(string text, char expected)
    {
        return text.Length == 1 && string.Equals(text, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private readonly CopilotAgentSessionManager _sessionManager;
    private readonly ITerminalSession _terminalSession;
    private readonly Hex1bShellState _state = new();
    private readonly Hex1bTerminalSessionWorkloadAdapter _terminalWorkloadAdapter;
    private readonly TerminalWidgetHandle _terminalWidgetHandle;
    private WindowHandle? _modelDialogWindow;
    private Action? _requestModelDialogFocus;

    public Hex1bApplicationShell(
        CopilotAgentSessionManager sessionManager,
        ITerminalSession terminalSession,
        ApplicationShellOptions? options = null)
    {
        _sessionManager = sessionManager;
        _terminalSession = terminalSession;
        _terminalWorkloadAdapter = new Hex1bTerminalSessionWorkloadAdapter(terminalSession);
        _state.TerminalPaneWidth = 90;
        _state.IsDebugPanelVisible = options?.ShowDebugPanelByDefault ?? ApplicationShellOptions.Default.ShowDebugPanelByDefault;

        TerminalWidgetHandle terminalWidgetHandle;
        _ = new Hex1bTerminalBuilder()
            .WithWorkload(_terminalWorkloadAdapter)
            .WithTerminalWidget(out terminalWidgetHandle)
            .WithDimensions(120, 40)
            .WithScrollback(1000, _ => { })
            .Build();
        _terminalWidgetHandle = terminalWidgetHandle;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var originalWindowTitle = Console.Title;

        await using var app = new Hex1bApp(
            _ =>
                _.WindowPanel()
                .Background(windowPanel => BuildApplicationContent(windowPanel))
                .WithInputBindings(bindings =>
                {
                    ConfigureGlobalBindings(bindings);
                    if (_sessionManager.PendingApproval is not null)
                    {
                        Hex1bShellInputBindings.ConfigureApprovalBindings(
                            bindings,
                            () => ResolveApprovalAsync(approved: true),
                            () => ResolveApprovalAsync(approved: false));
                    }
                }),
            new Hex1bAppOptions());

        _requestModelDialogFocus = () =>
        {
            app.RequestFocus(node => string.Equals(node.MetricName, ModelDialogListMetricName, StringComparison.Ordinal));
            app.Invalidate();
        };

        void HandleSessionManagerStateChanged()
        {
            UpdateHostWindowTitle();
            _state.IsUserInputActive = _sessionManager.PendingUserInputRequest is not null;

            if (_sessionManager.PendingApproval is not null)
            {
                _state.FocusTarget = Hex1bFocusTarget.Approval;
                app.RequestFocus(node => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal));
                return;
            }

            if (_sessionManager.PendingUserInputRequest is { } userInputRequest)
            {
                _state.IsUserInputActive = true;
                _state.SelectedUserChoiceIndex = Math.Clamp(userInputRequest.SelectedChoiceIndex, 0, Math.Max(0, userInputRequest.Choices.Count - 1));
                if (string.IsNullOrEmpty(_state.UserInputText))
                {
                    _state.UserInputText = userInputRequest.PromptText;
                }

                _state.FocusTarget = Hex1bFocusTarget.UserInput;
                app.RequestFocus(node => string.Equals(node.MetricName, UserInputMetricName, StringComparison.Ordinal));
                return;
            }

            _state.IsUserInputActive = false;
            _state.UserInputText = string.Empty;
            _state.SelectedUserChoiceIndex = 0;

            if (_state.FocusTarget is Hex1bFocusTarget.Approval or Hex1bFocusTarget.UserInput)
            {
                _state.FocusTarget = Hex1bFocusTarget.Prompt;
                app.RequestFocus(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal));
            }
        }

        _sessionManager.StateChanged += HandleSessionManagerStateChanged;
        UpdateHostWindowTitle();

        try
        {
            await app.RunAsync(cancellationToken);
        }
        finally
        {
            _requestModelDialogFocus = null;
            Console.Title = originalWindowTitle;
            _sessionManager.StateChanged -= HandleSessionManagerStateChanged;
            await _terminalWidgetHandle.DisposeAsync();
            await _terminalWorkloadAdapter.DisposeAsync();
        }
    }

    private Hex1b.Widgets.Hex1bWidget BuildApplicationContent<TParent>(Hex1b.WidgetContext<TParent> context)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        var mainContent = context.HSplitter(
            BuildLeftPane(context),
            BuildAgentPanel(context),
            _state.TerminalPaneWidth);

        if (!_state.IsDebugPanelVisible)
        {
            return mainContent;
        }

        return context.VStack(_ =>
        [
            mainContent,
            BuildDebugPanel()
        ]);
    }

    private Hex1b.Widgets.Hex1bWidget BuildLeftPane<TParent>(Hex1b.WidgetContext<TParent> root)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        return root.VStack(_ =>
        [
            BuildTerminalPanel(),
            BuildTerminalDragBar(root)
        ]);
    }

    private Hex1b.Widgets.Hex1bWidget BuildTerminalPanel()
    {
        return new Hex1b.Widgets.BorderWidget(
            new Hex1b.Widgets.TerminalWidget(_terminalWidgetHandle)
            {
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill,
                MetricName = TerminalMetricName
            })
        {
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill
        }.Title("Terminal");
    }

    private Hex1b.Widgets.Hex1bWidget BuildTerminalDragBar<TParent>(Hex1b.WidgetContext<TParent> root)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        return root.DragBarPanel(new Hex1b.Widgets.TextBlockWidget("⇆ Drag split"))
            .InitialSize(_state.TerminalPaneWidth)
            .MinSize(MinimumTerminalPaneWidth)
            .MaxSize(MaximumTerminalPaneWidth)
            .HandleEdge(Hex1b.Widgets.DragBarEdge.Right);
    }

    private Hex1b.Widgets.Hex1bWidget BuildAgentPanel<TParent>(Hex1b.WidgetContext<TParent> root)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        var sessionItems = _sessionManager.SavedSessions
            .Select(AgentShellTextFormatter.FormatSession)
            .ToArray();
        var hasPendingApproval = _sessionManager.PendingApproval is not null;
        var pendingUserInput = _sessionManager.PendingUserInputRequest;
        var hasPendingUserInput = pendingUserInput is not null;
        var approvalPrompt = AgentShellTextFormatter.BuildApprovalPrompt(_sessionManager.PendingApproval);

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
                        _state.FocusTarget = Hex1bFocusTarget.Approval;
                    }
                })
                .WithInputBindings(bindings =>
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

        var userInputWidget = BuildUserInputPanel(root);

        Hex1b.Widgets.Hex1bWidget promptEditor = hasPendingApproval || hasPendingUserInput
            ? new Hex1b.Widgets.TextBlockWidget(string.IsNullOrEmpty(_state.PromptText) ? string.Empty : _state.PromptText)
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }
            : new Hex1b.Widgets.TextBoxWidget(_state.PromptText)
            {
                MetricName = PromptMetricName,
                MinWidth = 20,
                WidthHint = SizeHint.Fill
            }
            .Multiline()
            .Height(4)
            .OnTextChanged(args =>
            {
                _state.FocusTarget = Hex1bFocusTarget.Prompt;
                _state.PromptText = args.NewText;
            })
            .WithInputBindings(bindings =>
            {
                Hex1bShellInputBindings.ConfigurePromptBindings(bindings, () => SendPromptAsync(_state.PromptText));
            });

        var sessionList = new Hex1b.Widgets.ListWidget(sessionItems)
        {
            InitialSelectedIndex = Math.Clamp(_state.SelectedSessionIndex, 0, Math.Max(0, sessionItems.Length - 1)),
            HeightHint = SizeHint.Content,
            MetricName = SessionsMetricName
        }
        .OnSelectionChanged(HandleSessionSelectionChanged)
        .OnItemActivated(async args => await OpenSessionAsync(args.ActivatedIndex));

        Hex1b.Widgets.Hex1bWidget conversationHistory = new Hex1b.Widgets.ScrollPanelWidget(
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.FormatConversation(_sessionManager.Messages))
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
            root.VStack(_ =>
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
                new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildStatusText(_sessionManager, _state.FocusTarget)) { HeightHint = SizeHint.Content }
            ]))
        {
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill
        }.Title("Agent");
    }

    private void HandleSessionSelectionChanged(ListSelectionChangedEventArgs args)
    {
        _state.FocusTarget = Hex1bFocusTarget.Sessions;
        _state.SelectedSessionIndex = args.SelectedIndex;
    }

    private void FocusWidget(InputBindingActionContext context, Hex1bFocusTarget focusTarget)
    {
        if (focusTarget == Hex1bFocusTarget.Prompt && _sessionManager.PendingApproval is not null)
        {
            focusTarget = Hex1bFocusTarget.Approval;
        }
        else if (focusTarget == Hex1bFocusTarget.Prompt && _sessionManager.PendingUserInputRequest is not null)
        {
            focusTarget = Hex1bFocusTarget.UserInput;
        }

        _state.FocusTarget = focusTarget;
        context.FocusWhere(node => focusTarget switch
        {
            Hex1bFocusTarget.Approval => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal),
            Hex1bFocusTarget.UserInput => string.Equals(node.MetricName, UserInputMetricName, StringComparison.Ordinal),
            Hex1bFocusTarget.Prompt => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal),
            Hex1bFocusTarget.Sessions => string.Equals(node.MetricName, SessionsMetricName, StringComparison.Ordinal),
            _ => string.Equals(node.MetricName, TerminalMetricName, StringComparison.Ordinal)
        });
    }

    private Hex1b.Widgets.Hex1bWidget BuildToolActivityPanel()
    {
        return new Hex1b.Widgets.BorderWidget(
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildToolActivityText(_sessionManager))
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

    private Hex1b.Widgets.Hex1bWidget BuildDebugPanel()
    {
        return new Hex1b.Widgets.BorderWidget(
            new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildDebugPanelText(_sessionManager))
            {
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill,
                MetricName = DebugPanelMetricName
            })
        {
            HeightHint = SizeHint.Fixed(8),
            WidthHint = SizeHint.Fill
        }.Title("Debug");
    }

    private Hex1b.Widgets.Hex1bWidget BuildUserInputPanel<TParent>(Hex1b.WidgetContext<TParent> root)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        var request = _sessionManager.PendingUserInputRequest;
        if (request is null)
        {
            return new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content };
        }

        _state.SelectedUserChoiceIndex = Math.Clamp(_state.SelectedUserChoiceIndex, 0, Math.Max(0, request.Choices.Count - 1));

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
                InitialSelectedIndex = _state.SelectedUserChoiceIndex,
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill,
                MetricName = request.AllowFreeformInput ? null : UserInputMetricName
            }
            .OnSelectionChanged(args =>
            {
                _state.FocusTarget = Hex1bFocusTarget.UserInput;
                _state.SelectedUserChoiceIndex = args.SelectedIndex;
            })
            .OnItemActivated(async args => await SubmitPendingUserChoiceAsync(args.ActivatedIndex)));
        }

        if (request.AllowFreeformInput)
        {
            children.Add(new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content });
            children.Add(new Hex1b.Widgets.BorderWidget(
                new Hex1b.Widgets.TextBoxWidget(_state.UserInputText)
                {
                    MetricName = UserInputMetricName,
                    MinWidth = 20,
                    WidthHint = SizeHint.Fill
                }
                .Multiline()
                .Height(4)
                .OnTextChanged(args =>
                {
                    _state.FocusTarget = Hex1bFocusTarget.UserInput;
                    _state.UserInputText = args.NewText;
                })
                .WithInputBindings(bindings =>
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

        return new Hex1b.Widgets.BorderWidget(root.VStack(_ => [.. children]))
        {
            HeightHint = SizeHint.Content,
            WidthHint = SizeHint.Fill
        }.Title("Question");
    }

    private void OpenModelDialog(WindowManager windows)
    {
        _modelDialogWindow ??= windows.Window(w => BuildModelDialog(w))
            .Title("Change model")
            .Size(76, 18)
            .Modal()
            .OnClose(() => _state.IsModelDialogOpen = false)
            .OnActivated(() => _requestModelDialogFocus?.Invoke());

        if (_modelDialogWindow is null)
        {
            return;
        }

        SyncSelectedModelIndex();
        _state.IsModelDialogOpen = true;
        windows.Open(_modelDialogWindow);
        _requestModelDialogFocus?.Invoke();
    }

    private Hex1b.Widgets.Hex1bWidget BuildModelDialog(Hex1b.WindowContentContext<Hex1b.Widgets.Hex1bWidget> context)
    {
        var models = _sessionManager.AvailableModels;
        var modelItems = models
            .Select(model => AgentShellTextFormatter.FormatModelOption(
                model,
                string.Equals(model.Id, _sessionManager.ActiveModelId, StringComparison.Ordinal)))
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
            new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
            new Hex1b.Widgets.ListWidget(modelItems)
            {
                InitialSelectedIndex = Math.Clamp(_state.SelectedModelIndex, 0, Math.Max(0, modelItems.Length - 1)),
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill,
                MetricName = ModelDialogListMetricName
            }
            .OnSelectionChanged(args => _state.SelectedModelIndex = args.SelectedIndex)
            .OnItemActivated(async args => await ChangeModelFromDialogAsync(context.Window, args.ActivatedIndex, args.CancellationToken)),
            new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
            _.HStack(h =>
            [
                h.Button("Apply").OnClick(async args => await ChangeModelFromDialogAsync(context.Window, _state.SelectedModelIndex, args.CancellationToken)),
                h.Button("Cancel").OnClick(_ => context.Window.Cancel())
            ])
        ]);
    }

    private async Task ChangeModelFromDialogAsync(WindowHandle dialogWindow, int selectedIndex, CancellationToken cancellationToken)
    {
        var models = _sessionManager.AvailableModels;
        if (selectedIndex < 0 || selectedIndex >= models.Count)
        {
            return;
        }

        _state.SelectedModelIndex = selectedIndex;
        var changed = await _sessionManager.ChangeModelAsync(models[selectedIndex].Id, cancellationToken);
        if (changed)
        {
            dialogWindow.Cancel();
        }
    }

    private void SyncSelectedModelIndex()
    {
        var models = _sessionManager.AvailableModels;
        if (models.Count == 0)
        {
            _state.SelectedModelIndex = 0;
            return;
        }

        var activeModelIndex = models
            .Select((model, index) => new { model.Id, index })
            .FirstOrDefault(item => string.Equals(item.Id, _sessionManager.ActiveModelId, StringComparison.Ordinal))?.index;

        _state.SelectedModelIndex = activeModelIndex ?? Math.Clamp(_state.SelectedModelIndex, 0, models.Count - 1);
    }

    private void UpdateHostWindowTitle()
    {
        Console.Title = AgentShellTextFormatter.BuildHostWindowTitle(_sessionManager);
    }

    private void ConfigureGlobalBindings(InputBindingsBuilder bindings)
    {
        Hex1bShellInputBindings.ConfigureGlobalBindings(
            bindings,
            ctx => FocusWidget(ctx, Hex1bFocusTarget.Terminal),
            ctx => FocusWidget(ctx, Hex1bFocusTarget.Prompt),
            ctx => FocusWidget(ctx, Hex1bFocusTarget.Sessions),
            () => _state.IsDebugPanelVisible = !_state.IsDebugPanelVisible,
            ctx => OpenModelDialog(ctx.Windows),
            () => _sessionManager.CreateNewSessionAsync(),
            () => _state.TerminalPaneWidth = Math.Max(MinimumTerminalPaneWidth, _state.TerminalPaneWidth - 5),
            () => _state.TerminalPaneWidth = Math.Min(MaximumTerminalPaneWidth, _state.TerminalPaneWidth + 5),
            ctx => ctx.RequestStop());
    }

    private async Task SendPromptAsync(string prompt)
    {
        var normalizedPrompt = prompt.TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return;
        }

        _state.PromptText = string.Empty;
        await _sessionManager.SendPromptAsync(normalizedPrompt);
    }

    private async Task SubmitPendingUserInputAsync()
    {
        var normalizedInput = _state.UserInputText.TrimEnd();
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return;
        }

        var submitted = await _sessionManager.SubmitUserInputAsync(normalizedInput);
        if (submitted)
        {
            _state.UserInputText = string.Empty;
        }
    }

    private async Task SubmitPendingUserChoiceAsync(int selectedIndex)
    {
        var submitted = await _sessionManager.SubmitUserChoiceAsync(selectedIndex);
        if (submitted)
        {
            _state.SelectedUserChoiceIndex = selectedIndex;
            _state.UserInputText = string.Empty;
        }
    }

    private async Task CancelPendingUserInputAsync()
    {
        var canceled = await _sessionManager.CancelUserInputRequestAsync();
        if (canceled)
        {
            _state.UserInputText = string.Empty;
        }
    }

    private async Task OpenSessionAsync(int activatedIndex)
    {
        if (activatedIndex < 0 || activatedIndex >= _sessionManager.SavedSessions.Count)
        {
            return;
        }

        _state.SelectedSessionIndex = activatedIndex;
        await _sessionManager.OpenStoredSessionAsync(_sessionManager.SavedSessions[activatedIndex].SessionId);
    }

    private async Task ResolveApprovalAsync(bool approved)
    {
        if (_sessionManager.PendingApproval is null)
        {
            return;
        }

        await _sessionManager.ResolvePendingApprovalAsync(approved);
    }
}
