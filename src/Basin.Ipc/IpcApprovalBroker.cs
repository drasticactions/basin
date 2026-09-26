using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

public sealed class IpcApprovalBroker : IApprovalBroker
{
    private readonly Dictionary<long, IpcApproval> _pending = [];
    private readonly HashSet<string> _allowedForRun = new(StringComparer.Ordinal);
    private IpcServer? _server;
    private long _nextId;

    public IpcApprovalBroker(TimeSpan? timeout = null)
    {
        Timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    public TimeSpan Timeout { get; set; }

    public IReadOnlyCollection<IpcApproval> Pending => _pending.Values;

    public IReadOnlyCollection<string> AllowedForRun => _allowedForRun;

    public event Action<IpcApproval>? Requested;

    public bool IsAllowedForRun(string method) => _allowedForRun.Contains(method);

    public IpcApproval Request(IpcHeldCall call, string reason)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(reason);
        var server = _server ?? throw new InvalidOperationException("the broker answers once its server starts");
        var approval = new IpcApproval(++_nextId, call, reason);
        if (!server.Events.HasSubscribers(IpcEventNames.ApprovalRequested) && Requested is null)
        {
            Finish(approval, IpcApprovalAnswer.NoAnswerer, "no connection is subscribed to approval/requested to answer, so the call is denied");
            return approval;
        }

        _pending[approval.Id] = approval;
        approval.Timer = server.Loop.AddTimer(() => Finish(
            approval, IpcApprovalAnswer.TimedOut, $"no one answered within {Timeout.TotalSeconds:0.#} s, so the call is denied"));
        approval.Timer.UpdateTimer((int)Math.Clamp(Timeout.TotalMilliseconds, 1, int.MaxValue));
        server.Events.Emit(
            IpcEventNames.ApprovalRequested,
            new IpcApprovalRequested(approval.Id, approval.Method, new IpcRawJson(approval.Arguments.IsEmpty ? "{}"u8.ToArray() : approval.Arguments), reason),
            IpcJsonContext.Default.IpcApprovalRequested);
        try
        {
            Requested?.Invoke(approval);
        }
        catch (Exception exception)
        {
            Log.Error($"an approval observer failed: {exception}");
        }

        return approval;
    }

    public bool Answer(long id, IpcApprovalAnswer answer)
    {
        if (answer is not (IpcApprovalAnswer.AllowOnce or IpcApprovalAnswer.AllowRun or IpcApprovalAnswer.Deny))
        {
            throw new ArgumentOutOfRangeException(nameof(answer), "an answer is allow once, allow for the run, or deny");
        }

        if (!_pending.TryGetValue(id, out var approval))
        {
            return false;
        }

        Finish(approval, answer, "a person denied the call");
        return true;
    }

    internal void Attach(IpcServer server)
    {
        if (_server is not null && !ReferenceEquals(_server, server))
        {
            throw new InvalidOperationException("a broker serves one server");
        }

        _server = server;
    }

    internal void Detach()
    {
        foreach (var approval in _pending.Values.ToArray())
        {
            Finish(approval, IpcApprovalAnswer.Deny, "the compositor stopped");
        }

        _server = null;
    }

    private void Finish(IpcApproval approval, IpcApprovalAnswer answer, string denial)
    {
        _pending.Remove(approval.Id);
        approval.Timer?.Remove();
        approval.Timer = null;
        if (answer == IpcApprovalAnswer.AllowRun)
        {
            _allowedForRun.Add(approval.Method);
        }

        if (!approval.Call.IsDone)
        {
            if (answer is IpcApprovalAnswer.AllowOnce or IpcApprovalAnswer.AllowRun)
            {
                approval.Call.Allow();
            }
            else
            {
                approval.Call.Deny(denial);
            }
        }

        try
        {
            approval.Finish(answer);
        }
        catch (Exception exception)
        {
            Log.Error($"an approval observer failed: {exception}");
        }
    }
}
