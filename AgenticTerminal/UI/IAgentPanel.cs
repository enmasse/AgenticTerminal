namespace AgenticTerminal.UI;

/// <summary>
/// Abstraction for the agent panel that can be rendered either inline in the TUI
/// or in an external Avalonia window.
/// </summary>
public interface IAgentPanel : IAsyncDisposable
{
    /// <summary>
    /// True when the panel is embedded inside the TUI layout.
    /// False when it runs as a separate external window.
    /// </summary>
    bool IsEmbedded { get; }

    /// <summary>
    /// Runs the external panel UI until the cancellation token is triggered.
    /// For embedded panels this returns immediately.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the shell whenever the agent session manager state has changed
    /// so that external panels can refresh their display.
    /// </summary>
    void NotifyStateChanged();
}
