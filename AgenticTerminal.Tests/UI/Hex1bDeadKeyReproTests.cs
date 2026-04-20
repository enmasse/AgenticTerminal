using AgenticTerminal.Terminal;
using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;
using Hex1b.Widgets;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bDeadKeyReproTests
{
    [Fact]
    public async Task TextInputHarness_RecordsPromptSubmitAndTerminalBytesSeparately()
    {
        await using var harness = await DeadKeyReproHarness.StartAsync();

        await harness.Automator.EnterAsync();
        await harness.Automator.WaitUntilTextAsync("Prompt submits: 1");

        harness.FocusTerminal();
        await harness.SendTerminalTextAsync("abc");
        await harness.Automator.WaitUntilTextAsync("Terminal bytes: 61-62-63");
    }

    private sealed class DeadKeyReproHarness : IAsyncDisposable
    {
        private const string PromptMetricName = "deadkey-prompt";
        private const string TerminalMetricName = "deadkey-terminal";

        private readonly CancellationTokenSource _cancellationTokenSource = new(TimeSpan.FromSeconds(15));
        private readonly FakeTerminalSession _terminalSession = new();
        private readonly Hex1bTerminalSessionWorkloadAdapter _workloadAdapter;
        private readonly TerminalWidgetHandle _terminalWidgetHandle;
        private readonly string _instructions = "Manual dead-key repro: run this app with a real US-Intl keyboard layout, focus F8 prompt or F7 terminal, then press a dead key followed by a letter and compare Prompt last submit vs Terminal bytes.";
        private readonly Hex1bTerminal _terminalBridge;
        private Task<int>? _bridgeRunTask;
        private Task<int>? _runTask;
        private Hex1bApp? _app;
        private int _promptSubmitCount;
        private string _lastPromptSubmit = "(none)";

        private DeadKeyReproHarness(
            Hex1bTerminal terminal,
            Hex1bTerminalAutomator automator,
            Hex1bTerminal terminalBridge,
            TerminalWidgetHandle terminalWidgetHandle,
            Hex1bTerminalSessionWorkloadAdapter workloadAdapter)
        {
            Terminal = terminal;
            Automator = automator;
            _terminalBridge = terminalBridge;
            _terminalWidgetHandle = terminalWidgetHandle;
            _workloadAdapter = workloadAdapter;
        }

        public Hex1bTerminal Terminal { get; }

        public Hex1bTerminalAutomator Automator { get; }

        public static async Task<DeadKeyReproHarness> StartAsync()
        {
            DeadKeyReproHarness? harness = null;
            TerminalWidgetHandle terminalWidgetHandle;
            var terminalSession = new FakeTerminalSession();
            var workloadAdapter = new Hex1bTerminalSessionWorkloadAdapter(terminalSession);
            var terminalBridge = new Hex1bTerminalBuilder()
                .WithWorkload(workloadAdapter)
                .WithTerminalWidget(out terminalWidgetHandle)
                .WithDimensions(80, 10)
                .Build();

            var terminal = Hex1bTerminal.CreateBuilder()
                .WithHeadless()
                .WithDimensions(100, 30)
                .WithHex1bApp((app, options) =>
                {
                    return ctx =>
                    {
                        harness!._app = app;
                        return harness.BuildUi(ctx);
                    };
                })
                .Build();

            harness = new DeadKeyReproHarness(
                terminal,
                new Hex1bTerminalAutomator(terminal, TimeSpan.FromSeconds(3)),
                terminalBridge,
                terminalWidgetHandle,
                workloadAdapter);

            await terminalSession.StartAsync();
            harness._bridgeRunTask = terminalBridge.RunAsync(harness._cancellationTokenSource.Token);
            harness._runTask = terminal.RunAsync(harness._cancellationTokenSource.Token);
            await harness.WaitUntilReadyAsync();
            return harness;
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

                if (_bridgeRunTask is not null)
                {
                    await _bridgeRunTask;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                await _workloadAdapter.DisposeAsync();
                await _terminalSession.DisposeAsync();
                await _terminalBridge.DisposeAsync();
                await Terminal.DisposeAsync();
            }
        }

        public void FocusTerminal()
        {
            Assert.NotNull(_app);
            Assert.True(_app!.FocusWhere(node => string.Equals(node.MetricName, TerminalMetricName, StringComparison.Ordinal)));
            _app.Invalidate();
        }

        public async Task SendTerminalTextAsync(string text)
        {
            await _terminalSession.SendTextAsync(text);
            _app?.Invalidate();
        }

        private async Task WaitUntilReadyAsync()
        {
            await Automator.WaitUntilTextAsync("Prompt submits: 0");
            Assert.NotNull(_app);
            Assert.True(_app!.FocusWhere(node => string.Equals(node.MetricName, PromptMetricName, StringComparison.Ordinal)));
            await Automator.WaitAsync(100);
        }

        private Hex1bWidget BuildUi(RootContext context)
        {
            return context.VStack(_ =>
            [
                new TextBlockWidget(_instructions) { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Prompt submits: {_promptSubmitCount}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Prompt last submit: {_lastPromptSubmit}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                new TextBlockWidget($"Terminal bytes: {_terminalSession.LastSentBytesHex}") { HeightHint = Hex1b.Layout.SizeHint.Content },
                BuildPromptProbe(),
                BuildTerminalProbe()
            ]);
        }

        private Hex1bWidget BuildPromptProbe()
        {
            return new BorderWidget(
                new InteractableWidget(_ => new TextBlockWidget("Prompt probe"))
                {
                    MetricName = PromptMetricName,
                    HeightHint = Hex1b.Layout.SizeHint.Content,
                    WidthHint = Hex1b.Layout.SizeHint.Fill
                }
                .WithInputBindings(bindings =>
                {
                    bindings.Key(Hex1bKey.Enter).Action(() =>
                    {
                        _promptSubmitCount++;
                        _lastPromptSubmit = "<enter received>";
                        _app?.Invalidate();
                    }, "Submit prompt probe");
                }))
            {
                HeightHint = Hex1b.Layout.SizeHint.Content,
                WidthHint = Hex1b.Layout.SizeHint.Fill
            }.Title("Prompt");
        }

        private Hex1bWidget BuildTerminalProbe()
        {
            return new BorderWidget(
                new TerminalWidget(_terminalWidgetHandle)
                {
                    MetricName = TerminalMetricName,
                    HeightHint = Hex1b.Layout.SizeHint.Content,
                    WidthHint = Hex1b.Layout.SizeHint.Fill
                })
            {
                HeightHint = Hex1b.Layout.SizeHint.Content,
                WidthHint = Hex1b.Layout.SizeHint.Fill
            }.Title("Terminal");
        }

        private sealed class FakeTerminalSession : ITerminalSession
        {
            private readonly TerminalScreenBuffer _displayState = new(80, 10);

            public event Action<TerminalOutputChunk>? OutputReceived;

            public ITerminalDisplayState DisplayState => _displayState;

            public string LastSentBytesHex { get; private set; } = "(none)";

            public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
            {
                LastSentBytesHex = BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(text));
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
}
