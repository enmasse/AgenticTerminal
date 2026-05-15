using Hex1b;
using Hex1b.Input;

namespace AgenticTerminal.UI;

/// <summary>
/// Extended <see cref="IAgentPanel"/> contract used only by the Hex1b shell.
/// Allows the shell to obtain a widget and hook up app-level callbacks.
/// </summary>
internal interface IHex1bEmbeddedAgentPanel : IAgentPanel
{
    /// <summary>Whether the session manager currently has a pending approval.</summary>
    bool HasPendingApproval { get; }

    /// <summary>
    /// Attaches Hex1b app-level callbacks so the panel can request focus and
    /// trigger redraws independently of the shell's event loop.
    /// </summary>
    void Attach(
        Action<Func<Hex1bNode, bool>> requestFocus,
        Action invalidate);

    /// <summary>Removes the callbacks set by <see cref="Attach"/>.</summary>
    void Detach();

    /// <summary>Builds the inline agent panel widget for the current frame.</summary>
    Hex1b.Widgets.Hex1bWidget BuildWidget<TParent>(Hex1b.WidgetContext<TParent> context)
        where TParent : Hex1b.Widgets.Hex1bWidget;

    /// <summary>Builds the model-picker dialog content widget.</summary>
    Hex1b.Widgets.Hex1bWidget BuildModelDialog(Hex1b.WindowContentContext<Hex1b.Widgets.Hex1bWidget> context);

    /// <summary>Opens (or focuses) the model-picker dialog window.</summary>
    void OpenModelDialog(WindowManager windows);

    /// <summary>Moves keyboard focus to the prompt (or approval/user-input widget if active).</summary>
    void FocusPromptPane(InputBindingActionContext context);

    /// <summary>Moves keyboard focus to the sessions list.</summary>
    void FocusSessionsPane(InputBindingActionContext context);

    /// <summary>Configures approval-related input bindings on the global builder.</summary>
    void ConfigureApprovalBindings(InputBindingsBuilder bindings);

    /// <summary>
    /// Subscribes to <c>CopilotAgentSessionManager.StateChanged</c> and calls the
    /// shell-provided callbacks when relevant state mutates.
    /// Called once, just before <see cref="IAgentPanel.RunAsync"/>.
    /// </summary>
    void Subscribe(Action onStateChanged);

    /// <summary>Unsubscribes from session manager events.</summary>
    void Unsubscribe(Action onStateChanged);
}
