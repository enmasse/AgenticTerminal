namespace AgenticTerminal.UI;

public interface IApplicationShell
{
    Task RunAsync(CancellationToken cancellationToken = default);

    Task WaitUntilReadyForInitializationAsync(CancellationToken cancellationToken = default);
}
