using AgenticTerminal.Approvals;

namespace AgenticTerminal.Tests.Approvals;

public sealed class ApprovalQueueTests
{
    [Fact]
    public async Task EnqueueShellCommandAsync_QueuesRequestsAndResolvesInOrder()
    {
        var queue = new ApprovalQueue();

        var firstDecision = queue.EnqueueShellCommandAsync("Get-Location");
        var secondDecision = queue.EnqueueShellCommandAsync("Get-ChildItem");

        Assert.Equal("Get-Location", queue.Current?.CommandText);

        Assert.True(queue.TryResolveCurrent(ApprovalDecision.Approved));
        Assert.Equal(ApprovalDecision.Approved, await firstDecision);
        Assert.Equal("Get-ChildItem", queue.Current?.CommandText);

        Assert.True(queue.TryResolveCurrent(ApprovalDecision.Denied));
        Assert.Equal(ApprovalDecision.Denied, await secondDecision);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void TryResolveCurrent_ReturnsFalseWhenQueueIsEmpty()
    {
        var queue = new ApprovalQueue();

        var resolved = queue.TryResolveCurrent(ApprovalDecision.Approved);

        Assert.False(resolved);
    }
}
