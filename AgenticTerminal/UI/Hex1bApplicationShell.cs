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
    private const string DebugPanelMetricName = "debug-panel";
    private const string TerminalSelectionMetricName = "terminal-selection";

    internal static bool MatchesApprovalInput(string text, char expected)
    {
        return text.Length == 1 && string.Equals(text, expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldRestorePromptFocusAfterAgentUpdate(Hex1bFocusTarget focusTarget)
    {
        return focusTarget is not (Hex1bFocusTarget.Prompt or Hex1bFocusTarget.Approval);
    }

    private readonly CopilotAgentSessionManager _sessionManager;
    private readonly ITerminalSession _terminalSession;
    private readonly Hex1bShellState _state = new();
    private readonly Hex1bTerminalSessionWorkloadAdapter _terminalWorkloadAdapter;
    private readonly TerminalWidgetHandle _terminalWidgetHandle;
    private readonly AgentPanelFactory _agentPanelFactory;
    private readonly TaskCompletionSource _layoutReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IAgentPanel _agentPanel;
    private bool _acceptingLayoutResizes;
    private CancellationTokenSource? _externalPanelCts;
    private Task _runningExternalPanelTask = Task.CompletedTask;

    internal static Hex1bAppOptions CreateAppOptions()
    {
        return new Hex1bAppOptions
        {
            EnableMouse = true
        };
    }

    public Task WaitUntilReadyForInitializationAsync(CancellationToken cancellationToken = default)
    {
        return _layoutReadyTcs.Task.WaitAsync(cancellationToken);
    }

    internal static Hex1bTerminalBuilder ConfigureTerminalBuilder(Hex1bTerminalBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMouse(true);
    }

    internal static Task SynchronizeTerminalSessionSizeAsync(
        ITerminalSession terminalSession,
        int columns,
        int rows,
        bool forceEquivalentResize = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terminalSession);

        if (!ShouldSynchronizeTerminalSessionSize(terminalSession.DisplayState, columns, rows, null, forceEquivalentResize))
        {
            return Task.CompletedTask;
        }

        return terminalSession.ResizeAsync(columns, rows, cancellationToken);
    }

    internal static bool ShouldSynchronizeTerminalSessionSize(
        ITerminalDisplayState displayState,
        int columns,
        int rows,
        (int Columns, int Rows)? pendingTerminalResize,
        bool forceEquivalentResize = false)
    {
        ArgumentNullException.ThrowIfNull(displayState);

        if (columns <= 0 || rows <= 0)
        {
            return false;
        }

        if (pendingTerminalResize is { Columns: var pendingColumns, Rows: var pendingRows }
            && pendingColumns == columns
            && pendingRows == rows)
        {
            return false;
        }

        return forceEquivalentResize || displayState.Columns != columns || displayState.Rows != rows;
    }

    public Hex1bApplicationShell(
        CopilotAgentSessionManager sessionManager,
        ITerminalSession terminalSession,
        AgentPanelFactory agentPanelFactory,
        ApplicationShellOptions? options = null)
    {
        _sessionManager = sessionManager;
        _terminalSession = terminalSession;
        _agentPanelFactory = agentPanelFactory;
        _state.TerminalPaneWidth = 90;
        _state.IsDebugPanelVisible = options?.ShowDebugPanelByDefault ?? ApplicationShellOptions.Default.ShowDebugPanelByDefault;
        _agentPanel = agentPanelFactory.Create();

        _terminalWorkloadAdapter = new Hex1bTerminalSessionWorkloadAdapter(terminalSession);

        TerminalWidgetHandle terminalWidgetHandle;
        _ = ConfigureTerminalBuilder(new Hex1bTerminalBuilder())
            .WithWorkload(_terminalWorkloadAdapter)
            .WithTerminalWidget(out terminalWidgetHandle)
            .WithScrollback(1000, _ => { })
            .Build();
        _terminalWidgetHandle = terminalWidgetHandle;
        _terminalWidgetHandle.Resized += HandleTerminalWidgetResized;
    }

    private static readonly string DiagLogPath = Path.Combine(Path.GetTempPath(), "AgenticTerminal.diag.log");

    private static void DiagLog(string message)
    {
        try { File.AppendAllText(DiagLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n"); } catch { }
    }

    private void HandleTerminalWidgetResized(int columns, int rows)
    {
        DiagLog($"Resized({columns},{rows}) widgetW={_terminalWidgetHandle.Width} widgetH={_terminalWidgetHandle.Height} acceptingLayoutResizes={_acceptingLayoutResizes}");
        if (_acceptingLayoutResizes && columns > 0 && rows > 0)
        {
            _ = _terminalSession.ResizeAsync(columns, rows);
            _layoutReadyTcs.TrySetResult();
            DiagLog($"TCS fired with ({columns},{rows})");
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var originalWindowTitle = Console.Title;

        Hex1bApp? app = null;

        app = new Hex1bApp(
            _ =>
                _.WindowPanel()
                .Background(windowPanel => BuildApplicationContent(windowPanel))
                .InputBindings(bindings =>
                {
                    ConfigureGlobalBindings(bindings, app);
                    if (_agentPanel is IHex1bEmbeddedAgentPanel embedded && embedded.HasPendingApproval)
                    {
                        embedded.ConfigureApprovalBindings(bindings);
                    }
                }),
            CreateAppOptions());

        AttachEmbeddedPanel(app);

        void HandleSessionManagerStateChanged()
        {
            UpdateHostWindowTitle();
            _agentPanel.NotifyStateChanged();
            app.Invalidate();
        }

        _sessionManager.StateChanged += HandleSessionManagerStateChanged;

        if (!_agentPanel.IsEmbedded)
        {
            _externalPanelCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runningExternalPanelTask = _agentPanel.RunAsync(_externalPanelCts.Token);
        }

        UpdateHostWindowTitle();

        try
        {
            _acceptingLayoutResizes = true;
            DiagLog("_acceptingLayoutResizes = true, about to call app.RunAsync");
            await app.RunAsync(cancellationToken);
        }
        finally
        {
            if (_externalPanelCts is not null)
            {
                await _externalPanelCts.CancelAsync();
                try { await _runningExternalPanelTask; } catch (OperationCanceledException) { }
                _externalPanelCts.Dispose();
                _externalPanelCts = null;
            }

            _sessionManager.StateChanged -= HandleSessionManagerStateChanged;
            DetachEmbeddedPanel();
            await _agentPanel.DisposeAsync();

            Console.Title = originalWindowTitle;
            _terminalWidgetHandle.Resized -= HandleTerminalWidgetResized;
            await _terminalWidgetHandle.DisposeAsync();
            await _terminalWorkloadAdapter.DisposeAsync();
        }

        await app.DisposeAsync();
    }

    // ─── Panel attach / detach ─────────────────────────────────────────────────

    private void AttachEmbeddedPanel(Hex1bApp app)
    {
        if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
        {
            embedded.Attach(
                predicate => app.RequestFocus(node => predicate(node)),
                () => app.Invalidate());
        }
    }

    private void DetachEmbeddedPanel()
    {
        if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
        {
            embedded.Detach();
        }
    }

    // ─── Toggle ────────────────────────────────────────────────────────────────

    private async Task ToggleAgentPanelAsync(Hex1bApp app, CancellationToken shellCancellationToken)
    {
        if (_externalPanelCts is not null)
        {
            await _externalPanelCts.CancelAsync();
            try { await _runningExternalPanelTask; } catch (OperationCanceledException) { }
            _externalPanelCts.Dispose();
            _externalPanelCts = null;
        }

        var nextMode = _agentPanel.IsEmbedded ? AgentPanelMode.Avalonia : AgentPanelMode.Embedded;

        DetachEmbeddedPanel();
        await _agentPanel.DisposeAsync();
        _agentPanel = _agentPanelFactory.Create(nextMode);
        AttachEmbeddedPanel(app);
        _agentPanel.NotifyStateChanged();

        if (!_agentPanel.IsEmbedded)
        {
            _externalPanelCts = CancellationTokenSource.CreateLinkedTokenSource(shellCancellationToken);
            _runningExternalPanelTask = _agentPanel.RunAsync(_externalPanelCts.Token);
        }

        app.Invalidate();
    }

    // ─── Layout ────────────────────────────────────────────────────────────────

    private Hex1b.Widgets.Hex1bWidget BuildApplicationContent<TParent>(Hex1b.WidgetContext<TParent> context)
        where TParent : Hex1b.Widgets.Hex1bWidget
    {
        Hex1b.Widgets.Hex1bWidget mainContent;

        if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
        {
            mainContent = context.HSplitter(
                BuildLeftPane(context),
                embedded.BuildWidget(context),
                _state.TerminalPaneWidth);
        }
        else
        {
            mainContent = BuildLeftPane(context);
        }

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
        var terminalWidget = new Hex1b.Widgets.TerminalWidget(_terminalWidgetHandle)
        {
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill,
            MetricName = TerminalMetricName
        }
        .InputBindings(bindings =>
        {
            bindings.Drag(Hex1b.Input.MouseButton.Left)
                .Shift()
                .Action((startX, startY) =>
                {
                    _state.TerminalSelection.AnchorX = startX;
                    _state.TerminalSelection.AnchorY = startY;
                    _state.TerminalSelection.CurrentX = startX;
                    _state.TerminalSelection.CurrentY = startY;
                    _state.TerminalSelection.IsActive = true;

                    return new Hex1b.Input.DragHandler(
                        (context, deltaX, deltaY) =>
                        {
                            _state.TerminalSelection.CurrentX = startX + deltaX;
                            _state.TerminalSelection.CurrentY = startY + deltaY;
                            context.Invalidate();
                        },
                        context =>
                        {
                            var (buffer, width, height) = _terminalWidgetHandle.GetScreenBufferSnapshot();
                            var selectedText = TerminalSelectionFormatter.ExtractSelectionText(buffer, width, height, _state.TerminalSelection);
                            if (!string.IsNullOrEmpty(selectedText))
                            {
                                context.CopyToClipboard(selectedText);
                            }

                            _state.TerminalSelection.Clear();
                            context.Invalidate();
                        });
                }, "Select terminal text");
        });

        var selectionOverlay = new Hex1b.Widgets.SurfaceWidget(layerContext =>
            [
                layerContext.Layer(ctx =>
                {
                    var overlay = TerminalSelectionFormatter.BuildOverlay(layerContext.Width, layerContext.Height, _state.TerminalSelection);
                    for (var y = 0; y < layerContext.Height; y++)
                    {
                        for (var x = 0; x < layerContext.Width; x++)
                        {
                            var cell = overlay[y, x];
                            if (cell.IsTransparent)
                            {
                                continue;
                            }

                            ctx[x, y] = cell;
                        }
                    }
                })
            ])
        {
            MetricName = TerminalSelectionMetricName,
            HeightHint = SizeHint.Fill,
            WidthHint = SizeHint.Fill
        };

        return new Hex1b.Widgets.BorderWidget(
            new Hex1b.Widgets.ZStackWidget([
                terminalWidget,
                selectionOverlay
            ])
            {
                HeightHint = SizeHint.Fill,
                WidthHint = SizeHint.Fill
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

    // ─── Focus / bindings ──────────────────────────────────────────────────────

    private void FocusTerminal(InputBindingActionContext context)
    {
        _state.IsTerminalFocused = true;
        context.FocusWhere(node => string.Equals(node.MetricName, TerminalMetricName, StringComparison.Ordinal));
    }

    private void FocusPrompt(InputBindingActionContext context)
    {
        _state.IsTerminalFocused = false;
        if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
        {
            embedded.FocusPromptPane(context);
        }
    }

    private void FocusSessions(InputBindingActionContext context)
    {
        _state.IsTerminalFocused = false;
        if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
        {
            embedded.FocusSessionsPane(context);
        }
    }

    private void ConfigureGlobalBindings(InputBindingsBuilder bindings, Hex1bApp? app)
    {
        Hex1bShellInputBindings.ConfigureGlobalBindings(
            bindings,
            ctx => FocusTerminal(ctx),
            ctx => FocusPrompt(ctx),
            ctx => FocusSessions(ctx),
            () => _state.IsDebugPanelVisible = !_state.IsDebugPanelVisible,
            ctx =>
            {
                if (_agentPanel is IHex1bEmbeddedAgentPanel embedded)
                {
                    embedded.OpenModelDialog(ctx.Windows);
                }
            },
            () => _sessionManager.CreateNewSessionAsync(),
            () => _state.TerminalPaneWidth = Math.Max(MinimumTerminalPaneWidth, _state.TerminalPaneWidth - 5),
            () => _state.TerminalPaneWidth = Math.Min(MaximumTerminalPaneWidth, _state.TerminalPaneWidth + 5),
            ctx => ctx.RequestStop(),
            app is not null
                ? ct => ToggleAgentPanelAsync(app, ct)
                : null);
    }

    private void UpdateHostWindowTitle()
    {
        Console.Title = AgentShellTextFormatter.BuildHostWindowTitle(_sessionManager);
    }
}
