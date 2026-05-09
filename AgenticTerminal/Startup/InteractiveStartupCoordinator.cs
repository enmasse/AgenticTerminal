namespace AgenticTerminal.Startup;

public static class InteractiveStartupCoordinator
{
    public static Task RunAsync(
        Func<CancellationToken, Task> initializeAgentAsync,
        Func<CancellationToken, Task> runShellAsync,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(initializeAgentAsync, runShellAsync, handleInitializationFailure: null, cancellationToken);
    }

    public static async Task RunAsync(
        Func<CancellationToken, Task> initializeAgentAsync,
        Func<CancellationToken, Task> runShellAsync,
        Action<Exception>? handleInitializationFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initializeAgentAsync);
        ArgumentNullException.ThrowIfNull(runShellAsync);

        using var initializationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var initializationTask = RunInitializationAsync(
            initializeAgentAsync,
            handleInitializationFailure,
            initializationCancellationTokenSource.Token);

        try
        {
            await runShellAsync(cancellationToken);
        }
        finally
        {
            if (!initializationTask.IsCompleted)
            {
                initializationCancellationTokenSource.Cancel();
            }

            await AwaitInitializationCompletionAsync(initializationTask, cancellationToken, initializationCancellationTokenSource.Token);
        }
    }

    private static async Task RunInitializationAsync(
        Func<CancellationToken, Task> initializeAgentAsync,
        Action<Exception>? handleInitializationFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await initializeAgentAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            handleInitializationFailure?.Invoke(exception);
        }
    }

    private static async Task AwaitInitializationCompletionAsync(
        Task initializationTask,
        CancellationToken applicationCancellationToken,
        CancellationToken initializationCancellationToken)
    {
        try
        {
            await initializationTask;
        }
        catch (OperationCanceledException) when (initializationCancellationToken.IsCancellationRequested && !applicationCancellationToken.IsCancellationRequested)
        {
        }
    }
}
