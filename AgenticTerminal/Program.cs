using AgenticTerminal.Agent;
using AgenticTerminal.Approvals;
using AgenticTerminal.Persistence;
using AgenticTerminal.Startup;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;
AppCommandLineOptions options;
AppConfiguration configuration;
try
{
    configuration = AppConfigurationLoader.Load(AppConfigurationLoader.GetDefaultConfigurationPath());
    options = AppCommandLineOptionsParser.Parse(args, AppCommandLineOptions.Interactive.ApplyConfiguration(configuration));
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AgenticTerminal");

Directory.CreateDirectory(appDataPath);

var approvalQueue = new ApprovalQueue();
var conversationSessionStore = new ConversationSessionStore(appDataPath);
var terminalStartupOptions = options.RunSmokeTest
    ? new TerminalSessionStartupOptions(TerminalSessionMode.HeadlessPipe, LoadUserProfile: false)
    : new TerminalSessionStartupOptions(TerminalSessionMode.InteractivePseudoConsole, LoadUserProfile: true);
var terminalSession = TerminalSessionFactory.Create(terminalStartupOptions);
using var smokeTestCancellationTokenSource = options.RunSmokeTest
    ? new CancellationTokenSource(TimeSpan.FromSeconds(options.SmokeTestTimeoutSeconds))
    : null;
var startupCancellationToken = smokeTestCancellationTokenSource?.Token ?? CancellationToken.None;

await using var sessionManager = new CopilotAgentSessionManager(
    approvalQueue,
    conversationSessionStore,
    terminalSession,
    Environment.CurrentDirectory,
    options.CopilotModel,
    new CopilotSessionOptions(configuration.FirstTokenTimeoutSeconds is > 0
        ? TimeSpan.FromSeconds(configuration.FirstTokenTimeoutSeconds.Value)
        : null));

try
{
    await sessionManager.InitializeAsync(startupCancellationToken);
}
catch (OperationCanceledException) when (options.RunSmokeTest)
{
    Console.Error.WriteLine($"Smoke test timed out during startup after {options.SmokeTestTimeoutSeconds} seconds.");
    return;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return;
}

if (options.RunSmokeTest)
{
    Console.WriteLine("Smoke test initialized.");

    try
    {
        await AgentSmokeTestRunner.RunAsync(sessionManager, options, Console.Out, startupCancellationToken);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine($"Smoke test timed out after {options.SmokeTestTimeoutSeconds} seconds.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
    }

    return;
}

var applicationShell = ApplicationShellFactory.Create(
    sessionManager,
    terminalSession,
    new ApplicationShellOptions(configuration.ShowDebugPanelByDefault ?? false));
await applicationShell.RunAsync(startupCancellationToken);
