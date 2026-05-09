using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;

namespace AgenticTerminal.Startup;

internal static class AppHost
{
    internal static Func<CancellationToken, Task> CreateInteractiveInitializationAsync(
        IApplicationShell applicationShell,
        Func<CancellationToken, Task> initializeAgentAsync)
    {
        ArgumentNullException.ThrowIfNull(applicationShell);
        ArgumentNullException.ThrowIfNull(initializeAgentAsync);

        return async cancellationToken =>
        {
            await applicationShell.WaitUntilReadyForInitializationAsync(cancellationToken);
            await initializeAgentAsync(cancellationToken);
        };
    }

    internal static TerminalSessionStartupOptions CreateTerminalStartupOptions(bool runSmokeTest, int hostColumns, int hostRows)
    {
        if (runSmokeTest)
        {
            return new TerminalSessionStartupOptions(TerminalSessionMode.HeadlessPipe, LoadUserProfile: false);
        }

        return new TerminalSessionStartupOptions(
            TerminalSessionMode.InteractivePseudoConsole,
            LoadUserProfile: true);
    }

    internal static (int Columns, int Rows) GetHostTerminalDimensions()
    {
        try
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }
        catch (IOException)
        {
            return (0, 0);
        }
        catch (InvalidOperationException)
        {
            return (0, 0);
        }
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var invokedExecutableName = Environment.GetCommandLineArgs().FirstOrDefault();
        var mode = AppModeResolver.Resolve(invokedExecutableName, args);
        if (mode == AppMode.Wrapper)
        {
            return await AgtWrapperRunner.RunAsync(args, cancellationToken);
        }

        AppCommandLineOptions options;
        AppConfiguration configuration;
        var configurationPath = AppConfigurationLoader.GetDefaultConfigurationPath();
        try
        {
            configuration = AppConfigurationLoader.Load(configurationPath);
            options = AppCommandLineOptionsParser.Parse(args, AppCommandLineOptions.Interactive.ApplyConfiguration(configuration));
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgenticTerminal");

        Directory.CreateDirectory(appDataPath);

        var approvalQueue = new ApprovalQueue();
        var conversationSessionStore = new ConversationSessionStore(appDataPath);
        var (hostColumns, hostRows) = GetHostTerminalDimensions();
        var terminalStartupOptions = CreateTerminalStartupOptions(options.RunSmokeTest, hostColumns, hostRows);
        var terminalSession = TerminalSessionFactory.Create(terminalStartupOptions);
        using var smokeTestCancellationTokenSource = options.RunSmokeTest
            ? new CancellationTokenSource(TimeSpan.FromSeconds(options.SmokeTestTimeoutSeconds))
            : null;
        var startupCancellationToken = smokeTestCancellationTokenSource?.Token ?? cancellationToken;

        await using var sessionManager = new CopilotAgentSessionManager(
            approvalQueue,
            conversationSessionStore,
            terminalSession,
            Environment.CurrentDirectory,
            options.CopilotModel,
            new CopilotSessionOptions(configuration.FirstTokenTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(configuration.FirstTokenTimeoutSeconds.Value)
                : null,
                async (modelId, _) =>
                {
                    configuration = configuration with
                    {
                        CopilotModel = modelId
                    };

                    AppConfigurationLoader.Save(configurationPath, configuration);
                    await Task.CompletedTask;
                }));

        if (options.RunSmokeTest)
        {
            try
            {
                await sessionManager.InitializeAsync(startupCancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"Smoke test timed out during startup after {options.SmokeTestTimeoutSeconds} seconds.");
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }

            Console.WriteLine("Smoke test initialized.");

            try
            {
                await AgentSmokeTestRunner.RunAsync(sessionManager, options, Console.Out, startupCancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"Smoke test timed out after {options.SmokeTestTimeoutSeconds} seconds.");
                return 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }

            return 0;
        }

        var applicationShell = ApplicationShellFactory.Create(
            sessionManager,
            terminalSession,
            new ApplicationShellOptions(configuration.ShowDebugPanelByDefault ?? false));

        try
        {
            var initializeAgentAsync = CreateInteractiveInitializationAsync(applicationShell, sessionManager.InitializeAsync);
            await InteractiveStartupCoordinator.RunAsync(
                initializeAgentAsync,
                applicationShell.RunAsync,
                sessionManager.HandleInitializationFailure,
                startupCancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
