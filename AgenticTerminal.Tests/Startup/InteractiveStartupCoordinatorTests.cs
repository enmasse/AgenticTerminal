using System.Threading;
using AgenticTerminal.Startup;

namespace AgenticTerminal.Tests.Startup;

public sealed class InteractiveStartupCoordinatorTests
{
    [Fact]
    public async Task RunAsync_StartsShellBeforeInitializationCompletes()
    {
        var initializationWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowInitializationToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializationCompleted = false;
        var shellStarted = false;

        await InteractiveStartupCoordinator.RunAsync(
            async cancellationToken =>
            {
                initializationWaiting.SetResult();
                await allowInitializationToComplete.Task.WaitAsync(cancellationToken);
                initializationCompleted = true;
            },
            async _ =>
            {
                await initializationWaiting.Task;
                shellStarted = true;
                Assert.False(initializationCompleted);
                allowInitializationToComplete.SetResult();
            });

        Assert.True(shellStarted);
    }

    [Fact]
    public async Task RunAsync_CancelsInitializationWhenShellCompletesEarly()
    {
        var initializationCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await InteractiveStartupCoordinator.RunAsync(
            async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    initializationCanceled.SetResult();
                }
            },
            _ => Task.CompletedTask);

        await initializationCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_ReportsInitializationFailure()
    {
        Exception? reportedException = null;
        var expectedException = new InvalidOperationException("startup failed");

        await InteractiveStartupCoordinator.RunAsync(
            _ => Task.FromException(expectedException),
            _ => Task.CompletedTask,
            exception => reportedException = exception);

        Assert.Same(expectedException, reportedException);
    }
}
