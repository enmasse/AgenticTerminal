using Hex1b.Input;

namespace AgenticTerminal.UI;

internal static class Hex1bShellInputBindings
{
    public static void ConfigureGlobalBindings(
        InputBindingsBuilder bindings,
        Action<InputBindingActionContext> focusTerminal,
        Action<InputBindingActionContext> focusPrompt,
        Action<InputBindingActionContext> focusSessions,
        Action<InputBindingActionContext> openModelDialog,
        Func<Task> createNewSessionAsync,
        Action shrinkTerminalPane,
        Action growTerminalPane,
        Action<InputBindingActionContext> requestQuit)
    {
        bindings.Key(Hex1bKey.F7).Global().OverridesCapture().Action(focusTerminal, "Focus terminal");
        bindings.Key(Hex1bKey.F8).Global().OverridesCapture().Action(focusPrompt, "Focus prompt");
        bindings.Key(Hex1bKey.F9).Global().OverridesCapture().Action(focusSessions, "Focus sessions");
        bindings.Key(Hex1bKey.F4).Global().OverridesCapture().Action(openModelDialog, "Change model");
        bindings.Key(Hex1bKey.F2).Global().OverridesCapture().Action(async () => await createNewSessionAsync(), "New session");
        bindings.Ctrl().Global().OverridesCapture().Key(Hex1bKey.LeftArrow).Action(shrinkTerminalPane, "Shrink terminal pane");
        bindings.Ctrl().Global().OverridesCapture().Key(Hex1bKey.RightArrow).Action(growTerminalPane, "Grow terminal pane");
        bindings.Key(Hex1bKey.F10).Global().OverridesCapture().Action(requestQuit, "Quit");
    }

    public static void ConfigureApprovalBindings(
        InputBindingsBuilder bindings,
        Func<Task> approveAsync,
        Func<Task> denyAsync)
    {
        bindings.Key(Hex1bKey.Y).Action(async () => await approveAsync(), "Approve command");
        bindings.Key(Hex1bKey.N).Action(async () => await denyAsync(), "Deny command");
        bindings.Character(text => Hex1bApplicationShell.MatchesApprovalInput(text, 'y'))
            .Action(async (text, ctx) => await approveAsync(), "Approve command");
        bindings.Character(text => Hex1bApplicationShell.MatchesApprovalInput(text, 'n'))
            .Action(async (text, ctx) => await denyAsync(), "Deny command");
    }

    public static void ConfigurePromptBindings(InputBindingsBuilder bindings, Func<Task> sendPromptAsync)
    {
        bindings.Remove(Hex1bKey.Enter, Hex1bModifiers.None);
        bindings.Key(Hex1bKey.Enter).Action(async () => await sendPromptAsync(), "Send prompt");
        bindings.Shift().Key(Hex1bKey.Enter).Triggers(Hex1b.Widgets.TextBoxWidget.InsertNewline);
    }
}
