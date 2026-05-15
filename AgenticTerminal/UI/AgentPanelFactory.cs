using AgenticTerminal.Agent;

namespace AgenticTerminal.UI;

/// <summary>
/// Creates <see cref="IAgentPanel"/> instances for the requested <see cref="AgentPanelMode"/>.
/// A single <see cref="AgentPanelViewModel"/> is shared across all created panels so that
/// both Hex1b and Avalonia panels observe the same session state.
/// </summary>
public sealed class AgentPanelFactory
{
    private readonly AgentPanelViewModel _viewModel;
    private readonly AgentPanelMode _defaultMode;

    public AgentPanelFactory(
        CopilotAgentSessionManager sessionManager,
        AgentPanelMode defaultMode = AgentPanelMode.Embedded)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        _viewModel = new AgentPanelViewModel(sessionManager);
        _defaultMode = defaultMode;
    }

    /// <summary>The shared ViewModel available to all panels.</summary>
    public AgentPanelViewModel ViewModel => _viewModel;

    /// <summary>Creates a panel using the factory's default mode.</summary>
    public IAgentPanel Create() => Create(_defaultMode);

    /// <summary>Creates a panel for the specified mode.</summary>
    public IAgentPanel Create(AgentPanelMode mode)
    {
        return mode switch
        {
            AgentPanelMode.Embedded => new Hex1bEmbeddedAgentPanel(_viewModel),
            AgentPanelMode.Avalonia => new AvaloniaAgentPanel(_viewModel),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
