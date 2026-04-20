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
    private const string SessionsMetricName = "sessions-list";
    private const string PromptMetricName = "agent-prompt";

    internal static bool MatchesApprovalInput(string text, char expected)
    {
        return text.Length == 1 && string.Equals(text, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private readonly CopilotAgentSessionManager _sessionManager;
    private readonly ITerminalSession _terminalSession;
    private readonly Hex1bShellState _state = new();
    private readonly Hex1bTerminalSessionWorkloadAdapter _terminalWorkloadAdapter;
    private readonly TerminalWidgetHandle _terminalWidgetHandle;

    public Hex1bApplicationShell(CopilotAgentSessionManager sessionManager, ITerminalSession terminalSession)
    {
        _sessionManager = sessionManager;
        _terminalSession = terminalSession;
        _terminalWorkloadAdapter = new Hex1bTerminalSessionWorkloadAdapter(terminalSession);
        _state.TerminalPaneWidth = 90;

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

        await using var app = new Hex1bApp(
            _ =>
                _.HSplitter(
                    BuildLeftPane(_),
                    BuildAgentPanel(_),
                    _state.TerminalPaneWidth).WithInputBindings(bindings =>
                {
                    bindings.Ctrl().Key(Hex1bKey.T).Action(ctx => FocusWidget(ctx, Hex1bFocusTarget.Terminal), "Focus terminal");
                    bindings.Ctrl().Key(Hex1bKey.P).Action(ctx => FocusWidget(ctx, Hex1bFocusTarget.Prompt), "Focus prompt");
                    bindings.Ctrl().Key(Hex1bKey.S).Action(ctx => FocusWidget(ctx, Hex1bFocusTarget.Sessions), "Focus sessions");
                    bindings.Ctrl().Key(Hex1bKey.N).Action(async () => await _sessionManager.CreateNewSessionAsync(), "New session");
                    bindings.Ctrl().Key(Hex1bKey.LeftArrow).Action(() => _state.TerminalPaneWidth = Math.Max(MinimumTerminalPaneWidth, _state.TerminalPaneWidth - 5), "Shrink terminal pane");
                    bindings.Ctrl().Key(Hex1bKey.RightArrow).Action(() => _state.TerminalPaneWidth = Math.Min(MaximumTerminalPaneWidth, _state.TerminalPaneWidth + 5), "Grow terminal pane");
                    if (_sessionManager.PendingApproval is not null)
                    {
                        bindings.Key(Hex1bKey.Y).Action(async () => await ResolveApprovalAsync(approved: true), "Approve command");
                        bindings.Key(Hex1bKey.N).Action(async () => await ResolveApprovalAsync(approved: false), "Deny command");
                        bindings.Character(text => MatchesApprovalInput(text, 'y'))
                            .Action(async (text, ctx) => await ResolveApprovalAsync(approved: true), "Approve command");
                        bindings.Character(text => MatchesApprovalInput(text, 'n'))
                            .Action(async (text, ctx) => await ResolveApprovalAsync(approved: false), "Deny command");
                    }

                    bindings.Ctrl().Key(Hex1bKey.Q).Action(ctx => ctx.RequestStop(), "Quit");
                }),
            new Hex1bAppOptions());

        void HandleSessionManagerStateChanged()
        {
            if (_sessionManager.PendingApproval is not null)
            {
                _state.FocusTarget = Hex1bFocusTarget.Approval;
                app.RequestFocus(node => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal));
                return;
            }

            if (_state.FocusTarget == Hex1bFocusTarget.Approval)
            {
                _state.FocusTarget = Hex1bFocusTarget.Prompt;
                app.RequestFocus(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal));
            }
        }

        _sessionManager.StateChanged += HandleSessionManagerStateChanged;

        try
        {
            await app.RunAsync(cancellationToken);
        }
        finally
        {
            _sessionManager.StateChanged -= HandleSessionManagerStateChanged;
            await _terminalWidgetHandle.DisposeAsync();
            await _terminalWorkloadAdapter.DisposeAsync();
        }
    }

    private Hex1b.Widgets.Hex1bWidget BuildLeftPane(Hex1b.RootContext root)
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

    private Hex1b.Widgets.Hex1bWidget BuildTerminalDragBar(Hex1b.RootContext root)
    {
        return root.DragBarPanel(new Hex1b.Widgets.TextBlockWidget("⇆ Drag split"))
            .InitialSize(_state.TerminalPaneWidth)
            .MinSize(MinimumTerminalPaneWidth)
            .MaxSize(MaximumTerminalPaneWidth)
            .HandleEdge(Hex1b.Widgets.DragBarEdge.Right);
    }

    private Hex1b.Widgets.Hex1bWidget BuildAgentPanel(Hex1b.RootContext root)
    {
        var sessionItems = _sessionManager.SavedSessions
            .Select(AgentShellTextFormatter.FormatSession)
            .ToArray();
        var hasPendingApproval = _sessionManager.PendingApproval is not null;
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
                    bindings.Key(Hex1bKey.Y).Action(async () => await ResolveApprovalAsync(approved: true), "Approve command");
                    bindings.Key(Hex1bKey.N).Action(async () => await ResolveApprovalAsync(approved: false), "Deny command");
                    bindings.Character(text => MatchesApprovalInput(text, 'y'))
                        .Action(async (text, ctx) => await ResolveApprovalAsync(approved: true), "Approve command");
                    bindings.Character(text => MatchesApprovalInput(text, 'n'))
                        .Action(async (text, ctx) => await ResolveApprovalAsync(approved: false), "Deny command");
                })))
            {
                HeightHint = SizeHint.Content,
                WidthHint = SizeHint.Fill
            }.Title("Approval")
            : new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content };

        Hex1b.Widgets.Hex1bWidget promptEditor = hasPendingApproval
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
            .OnSubmit(async args =>
            {
                _state.FocusTarget = Hex1bFocusTarget.Prompt;
                await SendPromptAsync(args.Text);
            })
            .WithInputBindings(bindings =>
            {
                bindings.Remove(Hex1bKey.Enter, Hex1bModifiers.None);
                bindings.Key(Hex1bKey.Enter).Action(async () => await SendPromptAsync(_state.PromptText), "Send prompt");
                bindings.Ctrl().Key(Hex1bKey.Enter).Triggers(Hex1b.Widgets.TextBoxWidget.InsertNewline);
            });

        var sessionList = new Hex1b.Widgets.ListWidget(sessionItems)
        {
            InitialSelectedIndex = Math.Clamp(_state.SelectedSessionIndex, 0, Math.Max(0, sessionItems.Length - 1)),
            HeightHint = SizeHint.Content,
            MetricName = SessionsMetricName
        }
        .OnSelectionChanged(HandleSessionSelectionChanged)
        .OnItemActivated(async args => await OpenSessionAsync(args.ActivatedIndex));

        return new Hex1b.Widgets.BorderWidget(
            root.VStack(_ =>
            [
                new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.BuildSessionSummary()) { HeightHint = SizeHint.Content },
                sessionList,
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                new Hex1b.Widgets.ScrollPanelWidget(
                    new Hex1b.Widgets.TextBlockWidget(AgentShellTextFormatter.FormatConversation(_sessionManager.Messages)),
                    Hex1b.Widgets.ScrollOrientation.Vertical,
                    true)
                {
                    HeightHint = SizeHint.Fill
                },
                new Hex1b.Widgets.TextBlockWidget(string.Empty) { HeightHint = SizeHint.Content },
                approvalWidget,
                new Hex1b.Widgets.BorderWidget(promptEditor)
                {
                    HeightHint = SizeHint.Content,
                    WidthHint = SizeHint.Fill
                }.Title("Prompt"),
                new Hex1b.Widgets.TextBlockWidget("Enter = send · Ctrl-Enter = new line") { HeightHint = SizeHint.Content },
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

        _state.FocusTarget = focusTarget;
        context.FocusWhere(node => focusTarget switch
        {
            Hex1bFocusTarget.Approval => string.Equals(node.MetricName, ApprovalMetricName, StringComparison.Ordinal),
            Hex1bFocusTarget.Prompt => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal),
            Hex1bFocusTarget.Sessions => string.Equals(node.MetricName, SessionsMetricName, StringComparison.Ordinal),
            _ => string.Equals(node.MetricName, TerminalMetricName, StringComparison.Ordinal)
        });
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
