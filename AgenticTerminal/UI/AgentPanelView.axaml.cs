using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AgenticTerminal.Persistence;
using ConversationMessage = AgenticTerminal.Persistence.ConversationMessage;

namespace AgenticTerminal.UI;

public sealed partial class AgentPanelView : UserControl
{
    public AgentPanelView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox is not null)
        {
            promptBox.KeyDown += OnPromptKeyDown;
        }

        KeyDown += OnViewKeyDown;

        if (DataContext is AgentPanelViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        // Give the prompt box focus immediately so the user can type right away.
        Dispatcher.UIThread.Post(FocusPromptBox, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox is not null)
        {
            promptBox.KeyDown -= OnPromptKeyDown;
        }

        KeyDown -= OnViewKeyDown;

        if (DataContext is AgentPanelViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentPanelViewModel.PendingApproval))
        {
            // When an approval is resolved the panel hides — return focus to the
            // prompt box so the user can keep typing without clicking.
            if (sender is AgentPanelViewModel { PendingApproval: null })
            {
                FocusPromptBox();
            }
        }
        else if (e.PropertyName == nameof(AgentPanelViewModel.PendingUserInputRequest))
        {
            // Agent is asking a question — focus prompt box so the user can type immediately.
            if (sender is AgentPanelViewModel { PendingUserInputRequest: not null })
            {
                FocusPromptBox();
            }
        }
    }

    private void FocusPromptBox()
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        Dispatcher.UIThread.Post(() => promptBox?.Focus(), DispatcherPriority.Input);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            if (DataContext is AgentPanelViewModel vm)
            {
                vm.SendPromptCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    // Y / N without any modifier — approve or deny when a prompt is pending.
    // Only fires when the TextBox does not have focus (to avoid eating typed text).
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (DataContext is not AgentPanelViewModel vm) return;
        if (vm.PendingApproval is null) return;

        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox?.IsFocused == true) return;

        if (e.Key == Key.Y)
        {
            vm.ApproveCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            vm.DenyCommand.Execute(null);
            e.Handled = true;
        }
    }
}

// --- Thin wrapper to make ConversationMessage bindable in XAML ---------------

public sealed class ConversationMessageViewModel
{
    public ConversationMessageViewModel(ConversationMessage message)
    {
        Role = message.Role;
        Content = message.Content;
        Header = $"{message.Timestamp.ToLocalTime():HH:mm}  {message.Role}";
    }

    public string Role { get; }
    public string Content { get; }
    public string Header { get; }
}

// --- Value converters --------------------------------------------------------

public sealed class RoleToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string role && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(30, 40, 60))
            : new SolidColorBrush(Color.FromRgb(28, 28, 28));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string role && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            ? Brushes.LightSkyBlue
            : Brushes.WhiteSmoke;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string role && string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}