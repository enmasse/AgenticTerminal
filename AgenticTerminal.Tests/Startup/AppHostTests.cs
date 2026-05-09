using AgenticTerminal.Startup;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;

namespace AgenticTerminal.Tests.Startup;

public sealed class AppHostTests
{
    [Fact]
    public void CreateTerminalStartupOptions_InteractiveMode_IgnoresHostDimensions()
    {
        var options = AppHost.CreateTerminalStartupOptions(runSmokeTest: false, hostColumns: 132, hostRows: 48);

        Assert.Equal(TerminalSessionMode.InteractivePseudoConsole, options.Mode);
        Assert.True(options.LoadUserProfile);
        Assert.Null(options.InitialColumns);
        Assert.Null(options.InitialRows);
    }

    [Fact]
    public void CreateTerminalStartupOptions_SmokeTestMode_IgnoresHostDimensions()
    {
        var options = AppHost.CreateTerminalStartupOptions(runSmokeTest: true, hostColumns: 132, hostRows: 48);

        Assert.Equal(TerminalSessionMode.HeadlessPipe, options.Mode);
        Assert.False(options.LoadUserProfile);
        Assert.Null(options.InitialColumns);
        Assert.Null(options.InitialRows);
    }

    [Fact]
    public async Task CreateInteractiveInitializationAsync_WaitsForShellMeasurementBeforeInitializing()
    {
        var shell = new TestApplicationShell();
        var initialized = false;
        var initialize = AppHost.CreateInteractiveInitializationAsync(
            shell,
            _ =>
            {
                initialized = true;
                return Task.CompletedTask;
            });

        var runTask = initialize(CancellationToken.None);
        await Task.Delay(50);

        Assert.False(initialized);

        shell.MarkReady();
        await runTask;

        Assert.True(initialized);
    }

    private sealed class TestApplicationShell : IApplicationShell
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WaitUntilReadyForInitializationAsync(CancellationToken cancellationToken = default)
            => _ready.Task.WaitAsync(cancellationToken);

        public void MarkReady() => _ready.TrySetResult();
    }
}
