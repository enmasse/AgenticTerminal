using System.Reflection;
using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;
using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;
using Hex1b.Widgets;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bInteractionAutomationTests
{
    [Fact]
    public async Task Prompt_ShiftEnterTriggersNewlineAction_AndEnterTriggersSendAction()
    {
        await using var harness = await InteractionHarness.StartAsync();

        await harness.Automator.KeyAsync(Hex1bKey.Enter, Hex1bModifiers.Shift);
        await harness.Automator.WaitUntilTextAsync("Newline count: 1");

        await harness.Automator.EnterAsync();
        await harness.Automator.WaitUntilTextAsync("Send count: 1");
    }

    [Fact]
    public async Task Terminal_ClickFocus_AllowsTypingIntoTerminalPanel()
    {
        await using var harness = await TerminalPanelTypingHarness.StartAsync();

        await harness.Automator.ClickAtAsync(10, 5, MouseButton.Left);
        await harness.Automator.TypeAsync("abc");
        await harness.Automator.WaitUntilAsync((Hex1bTerminalSnapshot _) => harness.TerminalSession.SentText == "abc");

        Assert.Equal("abc", harness.TerminalSession.SentText);
    }

    [Fact]
    public async Task Prompt_F4OpensDialog_AndArrowKeysMoveSelection()
    {
        await using var harness = await InteractionHarness.StartAsync();

        await harness.Automator.KeyAsync(Hex1bKey.F4);
        await harness.Automator.WaitUntilTextAsync("Dialog open");

        harness.FocusDialogList();

        await harness.Automator.DownAsync();
        await harness.Automator.WaitUntilTextAsync("Selected model: model-b");
    }

    [Fact]
    public async Task Prompt_ModelDialog_EnterAppliesSelection_AndClosesDialog()
    {
        await using var harness = await InteractionHarness.StartAsync();

        await harness.Automator.KeyAsync(Hex1bKey.F4);
        await harness.Automator.WaitUntilTextAsync("Dialog state: open");

        harness.FocusDialogList();

        await harness.Automator.DownAsync();
        await harness.Automator.WaitUntilTextAsync("Selected model: model-b");

        await harness.Automator.EnterAsync();
        await harness.Automator.WaitUntilTextAsync("Active model: model-b");
        await harness.Automator.WaitUntilTextAsync("Dialog state: closed");
    }

    [Fact]
    public async Task Prompt_RemainsSendable_AfterFocusIsMovedAwayAndAgentUpdateOccurs()
    {
        await using var harness = await InteractionHarness.StartAsync();

        harness.FocusSecondaryTarget();
        harness.RestorePromptFocusAfterAgentUpdate();
        await harness.Automator.WaitAsync(100);

        await harness.Automator.EnterAsync();
        await harness.Automator.WaitUntilTextAsync("Send count: 1");
    }

    private sealed class InteractionHarness : IAsyncDisposable
    {
        private const string PromptMetricName = "test-prompt";
        private const string DialogListMetricName = "test-model-dialog-list";
        private const string SecondaryMetricName = "test-secondary-focus";

        private readonly CancellationTokenSource _cancellationTokenSource = new(TimeSpan.FromSeconds(15));
        private readonly string[] _models = ["model-a", "model-b", "model-c"];
        private Task<int>? _runTask;
        private Hex1bApp? _app;
        private bool _isDialogOpen;
        private int _newlineCount;
        private int _sendCount;
        private int _activeModelIndex;
        private int _selectedModelIndex;

        private InteractionHarness(Hex1bTerminal terminal, Hex1bTerminalAutomator automator)
        {
            Terminal = terminal;
            Automator = automator;
        }

        public Hex1bTerminal Terminal { get; }

        public Hex1bTerminalAutomator Automator { get; }

        public static async Task<InteractionHarness> StartAsync()
        {
            InteractionHarness? harness = null;

            var terminal = Hex1bTerminal.CreateBuilder()
                .WithHeadless()
                .WithDimensions(100, 30)
                .WithMouse(true)
                .WithHex1bApp((app, options) =>
                {
                    return ctx =>
                    {
                        harness!._app = app;
                        return harness.BuildUi(ctx);
                    };
                })
                .Build();

            harness = new InteractionHarness(terminal, new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(3)));
            harness.Initialize();
            await harness.WaitUntilReadyAsync();
            return harness;
        }

        private void Initialize()
        {
            _runTask = Terminal.RunAsync(_cancellationTokenSource.Token);
        }

        private async Task WaitUntilReadyAsync()
        {
            await Automator.WaitUntilTextAsync("Send count: 0");
            Assert.NotNull(_app);
            Assert.True(_app!.FocusWhere(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal)));
            await Automator.WaitAsync(100);
        }

        public void FocusDialogList()
        {
            Assert.NotNull(_app);
            Assert.True(_app!.FocusWhere(node => string.Equals(node.MetricName, DialogListMetricName, StringComparison.Ordinal)));
            _app.Invalidate();
        }

        public void FocusSecondaryTarget()
        {
            Assert.NotNull(_app);
            Assert.True(_app!.FocusWhere(node => string.Equals(node.MetricName, SecondaryMetricName, StringComparison.Ordinal)));
            _app.Invalidate();
        }

        public void RestorePromptFocusAfterAgentUpdate()
        {
            _app?.RequestFocus(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal));
            _app?.Invalidate();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _app?.RequestStop();
                if (_runTask is not null)
                {
                    await _runTask;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                await Terminal.DisposeAsync();
            }
        }

        private Hex1bWidget BuildUi(RootContext context)
        {
            var children = new List<Hex1bWidget>
            {
                new TextBlockWidget($"Send count: {_sendCount}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Newline count: {_newlineCount}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                BuildPromptEditor(),
                new InteractableWidget(_ => new TextBlockWidget("secondary focus target"))
                {
                    MetricName = SecondaryMetricName,
                    HeightHint = Hex1b.Layout.SizeHint.Content,
                    WidthHint = Hex1b.Layout.SizeHint.Fill
                },
                new TextBlockWidget($"Dialog state: {(_isDialogOpen ? "open" : "closed")}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Active model: {_models[_activeModelIndex]}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Selected model: {_models[_selectedModelIndex]}") { HeightHint = Hex1b.Layout.SizeHint.Content }
            };

            if (_isDialogOpen)
            {
                children.Add(BuildDialog());
            }

            return context.VStack(_ => [.. children]);
        }

        private Hex1bWidget BuildPromptEditor()
        {
            return new BorderWidget(
                new InteractableWidget(_ =>
                    new TextBlockWidget("> prompt"))
                {
                    MetricName = PromptMetricName,
                    HeightHint = Hex1b.Layout.SizeHint.Content,
                    WidthHint = Hex1b.Layout.SizeHint.Fill
                }
                .WithInputBindings(bindings =>
                {
                    ConfigureGlobalBindings(bindings);
                    bindings.Key(Hex1bKey.F1).Triggers(TextBoxWidget.InsertNewline, () =>
                    {
                        _newlineCount++;
                        _app?.Invalidate();
                    }, "Insert newline");
                    Hex1bShellInputBindings.ConfigurePromptBindings(bindings, SendPromptAsync);
                }))
            {
                HeightHint = Hex1b.Layout.SizeHint.Content,
                WidthHint = Hex1b.Layout.SizeHint.Fill
            }.Title("Prompt");
        }

        private Hex1bWidget BuildDialog()
        {
            return new BorderWidget(
                new VStackWidget(
                    [
                        new TextBlockWidget($"Selected model: {_models[_selectedModelIndex]}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                        new ListWidget(_models)
                        {
                            InitialSelectedIndex = _selectedModelIndex,
                            HeightHint = Hex1b.Layout.SizeHint.Fill,
                            WidthHint = Hex1b.Layout.SizeHint.Fill,
                            MetricName = DialogListMetricName
                        }
                        .OnSelectionChanged(args =>
                        {
                            _selectedModelIndex = args.SelectedIndex;
                            _app?.Invalidate();
                        })
                        .OnItemActivated(async args => await ApplySelectedModelAsync())
                        .WithInputBindings(bindings =>
                        {
                            Hex1bShellInputBindings.ConfigureModelDialogBindings(
                                bindings,
                                CloseModelDialog);
                        }),
                        new TextBlockWidget("Dialog open") { HeightHint = Hex1b.Layout.SizeHint.Content }
                    ]))
            {
                HeightHint = Hex1b.Layout.SizeHint.Content,
                WidthHint = Hex1b.Layout.SizeHint.Fill
            }.Title("Change model");
        }

        private void ConfigureGlobalBindings(InputBindingsBuilder bindings)
        {
            Hex1bShellInputBindings.ConfigureGlobalBindings(
                bindings,
                ctx => ctx.FocusWhere(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal)),
                ctx => ctx.FocusWhere(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal)),
                ctx => ctx.FocusWhere(node => string.Equals(node.MetricName, DialogListMetricName, StringComparison.Ordinal)),
                () => { },
                ctx => OpenModelDialog(),
                () => Task.CompletedTask,
                () => { },
                () => { },
                ctx => ctx.RequestStop());
        }

        public void OpenModelDialog()
        {
            _isDialogOpen = true;
            RequestDialogFocus();
        }

        private Task ApplySelectedModelAsync()
        {
            _activeModelIndex = _selectedModelIndex;
            CloseModelDialog();
            return Task.CompletedTask;
        }

        private void CloseModelDialog()
        {
            _isDialogOpen = false;
            _app?.Invalidate();
        }

        private void RequestDialogFocus()
        {
            _app?.RequestFocus(node => string.Equals(node.MetricName, DialogListMetricName, StringComparison.Ordinal));
            _app?.Invalidate();
        }

        private Task SendPromptAsync()
        {
            _sendCount++;
            _app?.Invalidate();
            return Task.CompletedTask;
        }
    }

    private sealed class TerminalPanelTypingHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new(TimeSpan.FromSeconds(15));
        private readonly string _workspaceRoot;
        private Task<int>? _runTask;

        private TerminalPanelTypingHarness(
            Hex1bTerminal terminal,
            Hex1bTerminalAutomator automator,
            RecordingTerminalSession terminalSession,
            string workspaceRoot)
        {
            Terminal = terminal;
            Automator = automator;
            TerminalSession = terminalSession;
            _workspaceRoot = workspaceRoot;
        }

        public Hex1bTerminal Terminal { get; }

        public Hex1bTerminalAutomator Automator { get; }

        public RecordingTerminalSession TerminalSession { get; }

        public static async Task<TerminalPanelTypingHarness> StartAsync()
        {
            var workspaceRoot = Path.Combine(Path.GetTempPath(), "AgenticTerminalTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspaceRoot);

            var terminalSession = new RecordingTerminalSession();
            var sessionManager = new CopilotAgentSessionManager(
                new ApprovalQueue(),
                new ConversationSessionStore(workspaceRoot),
                terminalSession,
                workspaceRoot);
            var shell = new Hex1bApplicationShell(sessionManager, terminalSession);

            var buildTerminalPanelMethod = typeof(Hex1bApplicationShell).GetMethod(
                "BuildTerminalPanel",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(buildTerminalPanelMethod);

            var terminal = Hex1bTerminal.CreateBuilder()
                .WithHeadless()
                .WithDimensions(100, 20)
                .WithMouse(true)
                .WithHex1bApp((app, options) =>
                {
                    return ctx =>
                    {
                        return (Hex1bWidget)buildTerminalPanelMethod!.Invoke(shell, null)!;
                    };
                })
                .Build();

            var harness = new TerminalPanelTypingHarness(
                terminal,
                new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(3)),
                terminalSession,
                workspaceRoot);

            await terminalSession.StartAsync();
            harness._runTask = terminal.RunAsync(harness._cancellationTokenSource.Token);
            await harness.Automator.WaitAsync(100);
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_runTask is not null)
                {
                    _cancellationTokenSource.Cancel();
                    await _runTask;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                await TerminalSession.DisposeAsync();
                await Terminal.DisposeAsync();
                if (Directory.Exists(_workspaceRoot))
                {
                    Directory.Delete(_workspaceRoot, recursive: true);
                }
            }
        }
    }

    private sealed class RecordingTerminalSession : ITerminalSession
    {
        private readonly TerminalScreenBuffer _displayState = new(80, 10);

        public event Action<TerminalOutputChunk>? OutputReceived;

        public ITerminalDisplayState DisplayState => _displayState;

        public string SentText { get; private set; } = string.Empty;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            SentText += text;
            OutputReceived?.Invoke(new TerminalOutputChunk(text, false, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default)
            => SendTextAsync(input + "\r", cancellationToken);

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
            => Task.FromResult(new TerminalCommandResult(command, string.Empty, 0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
