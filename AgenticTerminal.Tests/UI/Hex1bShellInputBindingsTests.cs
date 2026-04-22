using AgenticTerminal.UI;
using Hex1b.Input;
using Hex1b.Widgets;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bShellInputBindingsTests
{
    [Theory]
    [InlineData(Hex1bKey.F7, Hex1bModifiers.None, "Focus terminal")]
    [InlineData(Hex1bKey.F8, Hex1bModifiers.None, "Focus prompt")]
    [InlineData(Hex1bKey.F9, Hex1bModifiers.None, "Focus sessions")]
    [InlineData(Hex1bKey.F6, Hex1bModifiers.None, "Toggle debug panel")]
    [InlineData(Hex1bKey.F4, Hex1bModifiers.None, "Change model")]
    [InlineData(Hex1bKey.F2, Hex1bModifiers.None, "New session")]
    [InlineData(Hex1bKey.LeftArrow, Hex1bModifiers.Control, "Shrink terminal pane")]
    [InlineData(Hex1bKey.RightArrow, Hex1bModifiers.Control, "Grow terminal pane")]
    [InlineData(Hex1bKey.F10, Hex1bModifiers.None, "Quit")]
    public void ConfigureGlobalBindings_RegistersExpectedShortcuts(Hex1bKey key, Hex1bModifiers modifiers, string description)
    {
        var bindings = new InputBindingsBuilder();

        Hex1bShellInputBindings.ConfigureGlobalBindings(
            bindings,
            _ => { },
            _ => { },
            _ => { },
            () => { },
            _ => { },
            () => Task.CompletedTask,
            () => { },
            () => { },
            _ => { });

        var binding = Assert.Single(bindings.Bindings, candidate =>
            candidate.FirstStep.Key == key
            && candidate.FirstStep.Modifiers == modifiers);

        Assert.Equal(description, binding.Description);
        Assert.True(binding.IsGlobal);
        Assert.True(binding.OverridesCapture);
    }

    [Fact]
    public void ConfigurePromptBindings_RegistersEnterToSendPrompt()
    {
        var bindings = CreatePromptBindingsBuilder();

        Hex1bShellInputBindings.ConfigurePromptBindings(bindings, () => Task.CompletedTask);

        var sendBinding = Assert.Single(bindings.Bindings, candidate =>
            candidate.FirstStep.Key == Hex1bKey.Enter
            && candidate.FirstStep.Modifiers == Hex1bModifiers.None);

        Assert.Equal("Send prompt", sendBinding.Description);
    }

    [Fact]
    public void ConfigurePromptBindings_RegistersShiftEnterToInsertNewline()
    {
        var bindings = CreatePromptBindingsBuilder();

        Hex1bShellInputBindings.ConfigurePromptBindings(bindings, () => Task.CompletedTask);

        var newlineBinding = Assert.Single(bindings.GetBindings(TextBoxWidget.InsertNewline), candidate =>
            candidate.FirstStep.Key == Hex1bKey.Enter
            && candidate.FirstStep.Modifiers == Hex1bModifiers.Shift);

        Assert.Equal(TextBoxWidget.InsertNewline, newlineBinding.ActionId);
    }

    [Fact]
    public void ConfigureApprovalBindings_RegistersKeyBindingsAndCharacterBindings()
    {
        var bindings = new InputBindingsBuilder();

        Hex1bShellInputBindings.ConfigureApprovalBindings(
            bindings,
            () => Task.CompletedTask,
            () => Task.CompletedTask);

        Assert.Contains(bindings.Bindings, candidate =>
            candidate.FirstStep.Key == Hex1bKey.Y
            && candidate.FirstStep.Modifiers == Hex1bModifiers.None
            && candidate.Description == "Approve command");
        Assert.Contains(bindings.Bindings, candidate =>
            candidate.FirstStep.Key == Hex1bKey.N
            && candidate.FirstStep.Modifiers == Hex1bModifiers.None
            && candidate.Description == "Deny command");

        var approveCharacterBinding = Assert.Single(bindings.CharacterBindings, candidate => candidate.Description == "Approve command");
        var denyCharacterBinding = Assert.Single(bindings.CharacterBindings, candidate => candidate.Description == "Deny command");

        Assert.True(approveCharacterBinding.Predicate("y"));
        Assert.True(approveCharacterBinding.Predicate("Y"));
        Assert.False(approveCharacterBinding.Predicate("n"));
        Assert.True(denyCharacterBinding.Predicate("n"));
        Assert.True(denyCharacterBinding.Predicate("N"));
        Assert.False(denyCharacterBinding.Predicate("y"));
    }

    private static InputBindingsBuilder CreatePromptBindingsBuilder()
    {
        var bindings = new InputBindingsBuilder();
        bindings.Key(Hex1bKey.F1).Triggers(TextBoxWidget.InsertNewline, () => { }, "Insert newline");
        return bindings;
    }
}
