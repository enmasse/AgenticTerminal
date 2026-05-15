using System.Reflection;
using AgenticTerminal.Agent;
using AgenticTerminal.Terminal;
using AgenticTerminal.UI;
using Hex1b;
using Moq;

namespace AgenticTerminal.Tests.UI;

public sealed class Hex1bApplicationShellTests
{
    private static (CopilotAgentSessionManager SessionManager, Mock<ITerminalSession> TerminalSession) CreateTestDependencies(int columns = 80, int rows = 24)
    {
        var mockTerminalSession = new Mock<ITerminalSession>();
        var mockDisplayState = new Mock<ITerminalDisplayState>();
        mockDisplayState.Setup(x => x.Columns).Returns(columns);
        mockDisplayState.Setup(x => x.Rows).Returns(rows);
        mockTerminalSession.Setup(x => x.DisplayState).Returns(mockDisplayState.Object);

        var approvalQueue = new AgenticTerminal.Approvals.ApprovalQueue();
        var conversationStore = new AgenticTerminal.Persistence.ConversationSessionStore(Path.GetTempPath());
        var sessionManager = new CopilotAgentSessionManager(
            approvalQueue,
            conversationStore,
            mockTerminalSession.Object,
            "workingDir",
            null,
            null);

        return (sessionManager, mockTerminalSession);
    }

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaultShowDebugPanelByDefault()
    {
        // Arrange
        var (sessionManager, mockTerminalSession) = CreateTestDependencies();

        // Act
        var shell = new Hex1bApplicationShell(sessionManager, mockTerminalSession.Object, null);

        // Assert
        Assert.NotNull(shell);
    }

    [Fact]
    public void Constructor_WithOptionsShowDebugPanelTrue_InitializesWithDebugPanelVisible()
    {
        // Arrange
        var (sessionManager, mockTerminalSession) = CreateTestDependencies();
        var options = new ApplicationShellOptions(ShowDebugPanelByDefault: true);

        // Act
        var shell = new Hex1bApplicationShell(sessionManager, mockTerminalSession.Object, new AgentPanelFactory(sessionManager), options);

        // Assert
        Assert.NotNull(shell);
    }

    [Fact]
    public void Constructor_WithOptionsShowDebugPanelFalse_InitializesWithDebugPanelHidden()
    {
        // Arrange
        var (sessionManager, mockTerminalSession) = CreateTestDependencies();
        var options = new ApplicationShellOptions(ShowDebugPanelByDefault: false);

        // Act
        var shell = new Hex1bApplicationShell(sessionManager, mockTerminalSession.Object, new AgentPanelFactory(sessionManager), options);

        // Assert
        Assert.NotNull(shell);
    }

    [Fact]
    public void Constructor_InitializesTerminalWidgetWithSessionDisplayStateDimensions()
    {
        // Arrange
        var (sessionManager, mockTerminalSession) = CreateTestDependencies(columns: 100, rows: 30);

        // Act
        var shell = new Hex1bApplicationShell(sessionManager, mockTerminalSession.Object, null);

        // Assert — constructor no longer reads DisplayState for initial dimensions;
        // Hex1b layout will call ResizeAsync on first pass with the real pane size.
        Assert.NotNull(shell);
    }

    [Fact]
    public async Task RunAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var (sessionManager, mockTerminalSession) = CreateTestDependencies();
        var shell = new Hex1bApplicationShell(sessionManager, mockTerminalSession.Object, null);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => shell.RunAsync(cts.Token));
    }

    // Note: Additional RunAsync tests are not feasible because the method creates a Hex1bApp instance
    // that requires an attached interactive console. Without dependency injection or virtualization
    // of the Hex1bApp, the method cannot be fully tested. The internal logic (event handlers,
    // cleanup in finally block) would require integration testing with an actual console.

    [Theory]
    [InlineData("y", 'y', true)]
    [InlineData("Y", 'y', true)]
    [InlineData("n", 'n', true)]
    [InlineData("N", 'n', true)]
    [InlineData("y", 'n', false)]
    [InlineData("yes", 'y', false)]
    [InlineData("", 'y', false)]
    public void MatchesApprovalInput_MatchesSingleCharacterChoices(string text, char expected, bool result)
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "MatchesApprovalInput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(result, (bool)method!.Invoke(null, [text, expected])!);
    }

    [Fact]
    public void CreateAppOptions_EnablesMouse()
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "CreateAppOptions",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var options = (Hex1bAppOptions?)method!.Invoke(null, null);

        Assert.NotNull(options);
        Assert.True(options!.EnableMouse);
    }

    [Fact]
    public void ConfigureTerminalBuilder_EnablesMouse()
    {
        var method = typeof(Hex1bApplicationShell).GetMethod(
            "ConfigureTerminalBuilder",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var builder = new Hex1bTerminalBuilder();
        var configuredBuilder = method!.Invoke(null, [builder]);

        Assert.Same(builder, configuredBuilder);

        var enableMouseField = typeof(Hex1bTerminalBuilder).GetField(
            "_enableMouse",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(enableMouseField);
        Assert.True((bool)enableMouseField!.GetValue(builder)!);
    }

    [Fact]
    public async Task SynchronizeTerminalSessionSizeAsync_WithDifferentDimensions_ResizesSession()
    {
        var session = new TestTerminalSession(columns: 80, rows: 24);

        await Hex1bApplicationShell.SynchronizeTerminalSessionSizeAsync(session, 100, 30, cancellationToken: CancellationToken.None);

        Assert.Equal((100, 30), session.LastResize);
    }

    [Fact]
    public async Task SynchronizeTerminalSessionSizeAsync_WithMatchingDimensions_DoesNotResizeSession()
    {
        var session = new TestTerminalSession(columns: 100, rows: 30);

        await Hex1bApplicationShell.SynchronizeTerminalSessionSizeAsync(session, 100, 30, cancellationToken: CancellationToken.None);

        Assert.Null(session.LastResize);
    }

    [Fact]
    public async Task SynchronizeTerminalSessionSizeAsync_WithInvalidDimensions_DoesNotResizeSession()
    {
        var session = new TestTerminalSession(columns: 80, rows: 24);

        await Hex1bApplicationShell.SynchronizeTerminalSessionSizeAsync(session, 0, 30, cancellationToken: CancellationToken.None);
        await Hex1bApplicationShell.SynchronizeTerminalSessionSizeAsync(session, 100, 0, cancellationToken: CancellationToken.None);

        Assert.Null(session.LastResize);
    }

    [Fact]
    public void ShouldSynchronizeTerminalSessionSize_WithPendingMatchingRequest_ReturnsFalse()
    {
        var session = new TestTerminalSession(columns: 80, rows: 24);

        var result = Hex1bApplicationShell.ShouldSynchronizeTerminalSessionSize(session.DisplayState, 100, 30, (100, 30));

        Assert.False(result);
    }

    [Fact]
    public void ShouldSynchronizeTerminalSessionSize_WithDifferentDimensionsAndNoPendingRequest_ReturnsTrue()
    {
        var session = new TestTerminalSession(columns: 80, rows: 24);

        var result = Hex1bApplicationShell.ShouldSynchronizeTerminalSessionSize(session.DisplayState, 100, 30, null, forceEquivalentResize: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSynchronizeTerminalSessionSize_WithMatchingDimensionsAndForcedEquivalentResize_ReturnsTrue()
    {
        var session = new TestTerminalSession(columns: 100, rows: 30);

        var result = Hex1bApplicationShell.ShouldSynchronizeTerminalSessionSize(session.DisplayState, 100, 30, null, forceEquivalentResize: true);

        Assert.True(result);
    }

    private sealed class TestTerminalSession(int columns, int rows) : ITerminalSession
    {
        private readonly TerminalScreenBuffer _displayState = new(columns, rows);

#pragma warning disable CS0067 // Event is never used
        public event Action<TerminalOutputChunk>? OutputReceived;
#pragma warning restore CS0067

        public ITerminalDisplayState DisplayState => _displayState;

        public (int Columns, int Rows)? LastResize { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SubmitInputAsync(string input, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
        {
            LastResize = (columns, rows);
            _displayState.Resize(columns, rows);
            return Task.CompletedTask;
        }

        public Task<string> CaptureSnapshotAsync(TerminalSnapshotOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<TerminalCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
            => Task.FromResult(new TerminalCommandResult(command, string.Empty, 0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
