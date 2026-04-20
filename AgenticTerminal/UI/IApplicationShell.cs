namespace AgenticTerminal.UI;

public interface IApplicationShell
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
