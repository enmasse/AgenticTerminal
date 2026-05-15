using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace AgenticTerminal.UI;

/// <summary>
/// Shows the Avalonia agent panel window in-process. Avalonia runs on the main STA thread
/// (started via <see cref="AppBuilder"/> in Program.cs); this panel posts show/hide
/// operations to the UI thread via <see cref="Dispatcher"/>.
/// </summary>
public sealed class AvaloniaAgentPanel : IAgentPanel, IAsyncDisposable
{
    private readonly AgentPanelViewModel _viewModel;
    private Window? _window;
    private TaskCompletionSource? _closedTcs;

    public bool IsEmbedded => false;

    public AvaloniaAgentPanel(AgentPanelViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        _closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is not ClassicDesktopStyleApplicationLifetime desktop)
            {
                _closedTcs.TrySetResult();
                return;
            }

            _window = new Window
            {
                Title = "AgenticTerminal — Agent",
                Width = 720,
                Height = 640,
                Content = new AgentPanelView { DataContext = _viewModel }
            };

            _window.Closed += (_, _) => _closedTcs.TrySetResult();
            _window.Show();
        });

        cancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() => _window?.Close());
        });

        return _closedTcs.Task;
    }

    public void NotifyStateChanged() { }

    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.Post(() => _window?.Close());
        return ValueTask.CompletedTask;
    }
}

/// <summary>Avalonia Application used as the process-global app instance.</summary>
internal sealed class AvaloniaPanelApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

