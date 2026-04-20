using AgenticTerminal.Terminal;

namespace AgenticTerminal.Tests.Terminal;

public sealed class TerminalCommandCaptureTests
{
    [Fact]
    public void AppendChunk_CollectsOutputUntilCompletionMarkerArrivesAcrossChunks()
    {
        var capture = new TerminalCommandCapture("abc123", "Get-ChildItem");

        Assert.False(capture.TryComplete("line 1\r\n__AGENTI"));
        Assert.True(capture.TryComplete("CTERMINAL_DONE__:abc123:0\r\n"));

        Assert.True(capture.IsCompleted);
        Assert.Equal(0, capture.ExitCode);
        Assert.Equal("line 1\r\n", capture.Output);
    }

    [Fact]
    public async Task AppendChunk_ReplacesInternalWrapperLineWithCommandText()
    {
        var capture = new TerminalCommandCapture("abc123", "Get-Process");

        var visible = capture.AppendChunk(
            "$__agenticterminal_command = decode\r\nprocess-output\r\n" +
            BuildMarker("abc123", 0));

        Assert.Equal("Get-Process\r\nprocess-output\r\n", visible);
        Assert.Equal("Get-Process\r\nprocess-output\r\n", capture.Output);
        Assert.True(capture.IsCompleted);
        Assert.Equal(0, capture.ExitCode);
        Assert.True(capture.Completion.Task.IsCompletedSuccessfully);

        var result = await capture.Completion.Task;

        Assert.Equal("Get-Process\r\nprocess-output\r\n", result.Output);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AppendChunk_PreservesPromptPrefixWhenReplacingInternalWrapperLine()
    {
        var capture = new TerminalCommandCapture("def456", "git status");

        var visible = capture.AppendChunk(
            "PS D:\\source\\AgenticTerminal> $__agenticterminal_command = decode\r\n" +
            "On branch main\r\n" +
            BuildMarker("def456", 0));

        Assert.Equal("PS D:\\source\\AgenticTerminal> git status\r\nOn branch main\r\n", visible);
        Assert.Equal("PS D:\\source\\AgenticTerminal> git status\r\nOn branch main\r\n", capture.Output);
    }

    [Fact]
    public void AppendChunk_CompletesWhenMarkerArrivesInLaterChunk()
    {
        var capture = new TerminalCommandCapture("ghi789", "Get-ChildItem");

        var firstVisible = capture.AppendChunk(
            "$__agenticterminal_command = decode\r\nfile.txt\r\n__AGENTICTERMINAL_DONE__:ghi789:");
        var secondVisible = capture.AppendChunk("0\r\nPS> ");

        Assert.Equal("Get-ChildItem\r\n", firstVisible);
        Assert.Equal("file.txt\r\nPS> ", secondVisible);
        Assert.True(capture.IsCompleted);
        Assert.Equal(0, capture.ExitCode);
        Assert.Equal("Get-ChildItem\r\nfile.txt\r\n", capture.Output);
    }

    [Fact]
    public void AppendChunk_PreservesNormalOutputWithoutInternalWrapper()
    {
        var capture = new TerminalCommandCapture("jkl012", "Write-Output 'done'");

        var visible = capture.AppendChunk("done\r\n" + BuildMarker("jkl012", 1));

        Assert.Equal("done\r\n", visible);
        Assert.Equal("done\r\n", capture.Output);
        Assert.True(capture.IsCompleted);
        Assert.Equal(1, capture.ExitCode);
    }

    [Fact]
    public void AppendChunk_IgnoresMarkerLiteralInsideEchoedWrapperUntilRealCompletionLineArrives()
    {
        var capture = new TerminalCommandCapture("mno345", "Get-ChildItem -Force");

        var wrapperEcho =
            "$__agenticterminal_command = decode; " +
            "finally { Write-Host '" + TerminalCommandCapture.CompletionMarkerPrefix + ":mno345:' + $__agenticterminal_exit }\r\n";

        var firstVisible = capture.AppendChunk(wrapperEcho);

        Assert.Equal(string.Empty, firstVisible);
        Assert.False(capture.IsCompleted);

        var secondVisible = capture.AppendChunk("OUTPUT\r\n" + BuildMarker("mno345", 0));

        Assert.Equal("Get-ChildItem -Force\r\nOUTPUT\r\n", secondVisible);
        Assert.True(capture.IsCompleted);
        Assert.Equal(0, capture.ExitCode);
        Assert.Equal("Get-ChildItem -Force\r\nOUTPUT\r\n", capture.Output);
    }

    private static string BuildMarker(string commandId, int exitCode)
    {
        return $"{TerminalCommandCapture.CompletionMarkerPrefix}:{commandId}:{exitCode}\r\n";
    }
}
