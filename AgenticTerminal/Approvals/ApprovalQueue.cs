namespace AgenticTerminal.Approvals;

public sealed class ApprovalQueue
{
    private readonly object _syncRoot = new();
    private readonly Queue<PendingApproval> _pendingApprovals = new();
    private PendingApproval? _current;

    public event Action? Changed;

    public ApprovalRequest? Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current?.Request;
            }
        }
    }

    public Task<ApprovalDecision> EnqueueShellCommandAsync(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        PendingApproval pendingApproval;

        lock (_syncRoot)
        {
            pendingApproval = new PendingApproval(new ApprovalRequest(Guid.NewGuid().ToString("N"), commandText, DateTimeOffset.UtcNow));
            if (_current is null)
            {
                _current = pendingApproval;
            }
            else
            {
                _pendingApprovals.Enqueue(pendingApproval);
            }
        }

        Changed?.Invoke();
        return pendingApproval.Completion.Task;
    }

    public bool TryResolveCurrent(ApprovalDecision decision)
    {
        PendingApproval? completedApproval;

        lock (_syncRoot)
        {
            if (_current is null)
            {
                return false;
            }

            completedApproval = _current;
            _current = _pendingApprovals.Count > 0 ? _pendingApprovals.Dequeue() : null;
        }

        completedApproval.Completion.TrySetResult(decision);
        Changed?.Invoke();
        return true;
    }

    private sealed class PendingApproval
    {
        public PendingApproval(ApprovalRequest request)
        {
            Request = request;
        }

        public ApprovalRequest Request { get; }

        public TaskCompletionSource<ApprovalDecision> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
