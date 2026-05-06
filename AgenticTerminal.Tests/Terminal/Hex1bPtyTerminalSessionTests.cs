using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class Hex1bPtyTerminalSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("Get-ChildItem", "Get-ChildItem\r")]
    [InlineData("exit", "exit\r")]
    public void BuildSubmittedInput_AppendsCarriageReturn(string input, string expected)
    {
        var method = typeof(Hex1bPtyTerminalSession).GetMethod(
            "BuildSubmittedInput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, (string)method!.Invoke(null, [input])!);
    }

    [Fact]
    public void BuildWrappedCommandScript_UsesStartupFunctionInvocation()
    {
        var method = typeof(Hex1bPtyTerminalSession).GetMethod(
            "BuildWrappedCommandScript",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var script = (string)method!.Invoke(null, ["Get-ChildItem", "abc123", "agenticterminal-test-pipe"])!;

        Assert.StartsWith("__agenticterminal_invoke", script, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipeClientStream", script, StringComparison.Ordinal);
        Assert.Contains("agenticterminal-test-pipe", script, StringComparison.Ordinal);
        Assert.Contains("'abc123'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTextAsync_ForwardsTypedInputToFakeShell()
    {
        KillLingeringTestHostProcesses();
        var output = new ConcurrentQueue<string>();
        var session = CreateSession();
        using var subscription = Subscribe(session, output);

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);

            await session.StartAsync(cancellationTokenSource.Token);
            await WaitForOutputAsync(output, "$", cancellationTokenSource.Token);
            await session.SubmitInputAsync("hello from test", cancellationTokenSource.Token);

            await WaitForOutputAsync(output, "INPUT:hello from test", cancellationTokenSource.Token);
            var snapshot = await session.CaptureSnapshotAsync(cancellationToken: cancellationTokenSource.Token);

            Assert.Contains("INPUT:hello from test", snapshot, StringComparison.Ordinal);
        }
        finally
        {
            await DisposeSessionAsync(session);
            KillLingeringTestHostProcesses();
        }
    }

    [Fact]
    public async Task SendTextAsync_ForwardsWrappedCommandScriptToFakeShell()
    {
        var output = new ConcurrentQueue<string>();
        var session = CreatePowerShellSession();
        using var subscription = Subscribe(session, output);

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);

            await session.StartAsync(cancellationTokenSource.Token);
            await WaitForOutputAsync(output, "PS>", cancellationTokenSource.Token);

            var buildMethod = typeof(Hex1bPtyTerminalSession).GetMethod(
                "BuildWrappedCommandScript",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(buildMethod);

            await using var backchannel = new TerminalCommandBackchannel();
            var script = (string)buildMethod!.Invoke(null, ["Write-Output 'wrapped-ok'", "abc123", backchannel.PipeName])!;
            await session.SendTextAsync(BuildSubmitted(script), cancellationTokenSource.Token);

            var completionMessage = await backchannel.ReadMessageAsync(cancellationTokenSource.Token);

            Assert.Equal("abc123", completionMessage.CommandId);
            Assert.Equal(0, completionMessage.ExitCode);

            await WaitForOutputAsync(output, "wrapped-ok", cancellationTokenSource.Token);
            Assert.DoesNotContain(TerminalCommandCapture.CompletionMarkerPrefix, string.Concat(output.ToArray()), StringComparison.Ordinal);
        }
        finally
        {
            await DisposeSessionAsync(session);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_RunsWrappedCommandThroughFakeShell()
    {
        var output = new ConcurrentQueue<string>();
        var session = CreatePowerShellSession();
        using var subscription = Subscribe(session, output);

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);

            await session.StartAsync(cancellationTokenSource.Token);
            await WaitForOutputAsync(output, "PS>", cancellationTokenSource.Token);

            TerminalCommandResult result;
            try
            {
                result = await session.ExecuteCommandAsync("Write-Output 'named-pipe-test'", cancellationTokenSource.Token)
                    .WaitAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException($"Timed out waiting for ExecuteCommandAsync. Current output: {string.Concat(output.ToArray())}");
            }

            Assert.Equal("Write-Output 'named-pipe-test'", result.CommandText);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("named-pipe-test", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("$__agenticterminal_command", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(TerminalCommandCapture.CompletionMarkerPrefix, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            await DisposeSessionAsync(session);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithSequentialCommands_RunsBothCommands()
    {
        var output = new ConcurrentQueue<string>();
        var session = CreatePowerShellSession();
        using var subscription = Subscribe(session, output);

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TestTimeout);

            await session.StartAsync(cancellationTokenSource.Token);
            await WaitForOutputAsync(output, "PS>", cancellationTokenSource.Token);

            var firstResult = await session.ExecuteCommandAsync("Write-Output 'first-command'", cancellationTokenSource.Token)
                .WaitAsync(cancellationTokenSource.Token);
            var secondResult = await session.ExecuteCommandAsync("Write-Output 'second-command'", cancellationTokenSource.Token)
                .WaitAsync(cancellationTokenSource.Token);

            Assert.Contains("first-command", firstResult.Output, StringComparison.Ordinal);
            Assert.Contains("second-command", secondResult.Output, StringComparison.Ordinal);
        }
        finally
        {
            await DisposeSessionAsync(session);
        }
    }

    private static string BuildSubmitted(string text)
    {
        var method = typeof(Hex1bPtyTerminalSession).GetMethod(
            "BuildSubmittedInput",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (string)method!.Invoke(null, [text])!;
    }

    private static Hex1bPtyTerminalSession CreateSession()
    {
        var startupOptions = new TerminalSessionStartupOptions(TerminalSessionMode.InteractivePseudoConsole, SuppressPrompt: true, LoadUserProfile: false);
        var hostAssemblyPath = typeof(TestHostMarker).Assembly.Location;
        var hostDllPath = Path.ChangeExtension(hostAssemblyPath, ".dll");
        if (!File.Exists(hostDllPath))
        {
            hostDllPath = hostAssemblyPath;
        }

        var launchConfiguration = new TerminalProcessLaunchConfiguration(
            "dotnet",
            [hostDllPath],
            Path.GetDirectoryName(hostDllPath)!,
            null,
            true);

        return new Hex1bPtyTerminalSession(startupOptions, launchConfiguration);
    }

    private static Hex1bPtyTerminalSession CreatePowerShellSession()
    {
        return new Hex1bPtyTerminalSession(new TerminalSessionStartupOptions(
            TerminalSessionMode.InteractivePseudoConsole,
            SuppressPrompt: true,
            LoadUserProfile: false));
    }

    private static IDisposable Subscribe(ITerminalSession session, ConcurrentQueue<string> output)
    {
        void Handler(TerminalOutputChunk chunk)
        {
            output.Enqueue(chunk.Text);
        }

        session.OutputReceived += Handler;
        return new Subscription(() => session.OutputReceived -= Handler);
    }

    private static async Task WaitForOutputAsync(ConcurrentQueue<string> output, string expected, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var combined = string.Concat(output.ToArray());
                if (combined.Contains(expected, StringComparison.Ordinal))
                {
                    return;
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException($"Timed out waiting for output containing '{expected}'. Current output: {string.Concat(output.ToArray())}");
        }
    }

    private static async Task DisposeSessionAsync(Hex1bPtyTerminalSession session)
    {
        try
        {
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
        }
    }

    private static void KillLingeringTestHostProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName("AgenticTerminal.TestHost"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
