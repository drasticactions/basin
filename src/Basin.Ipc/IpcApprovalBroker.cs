using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

/// <summary>
/// Turns held calls into approval requests that a subscribed connection or a local dialog answers. The server registers the <c>approval</c> group when it has a broker.
/// </summary>
public sealed class IpcApprovalBroker : IApprovalBroker
{
    private readonly Dictionary<long, IpcApproval> _pending = [];
    private readonly HashSet<string> _allowedForRun = new(StringComparer.Ordinal);
    private IpcServer? _server;
    private long _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcApprovalBroker"/> class.
    /// </summary>
    /// <param name="timeout">The time after which an unanswered request is denied. The default is 60 seconds.</param>
    public IpcApprovalBroker(TimeSpan? timeout = null)
    {
        Timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Gets or sets the time after which an unanswered request is denied. A new value applies to later requests.
    /// </summary>
    public TimeSpan Timeout { get; set; }

    /// <summary>
    /// Gets the requests that wait for an answer.
    /// </summary>
    public IReadOnlyCollection<IpcApproval> Pending => _pending.Values;

    /// <summary>
    /// Gets the methods that an <see cref="IpcApprovalAnswer.AllowRun"/> answer allowed for the rest of the run.
    /// </summary>
    public IReadOnlyCollection<string> AllowedForRun => _allowedForRun;

    /// <summary>
    /// Occurs after a request starts and its event goes to the subscribers. A compositor with its own dialog handles it and calls <see cref="Answer"/>.
    /// </summary>
    public event Action<IpcApproval>? Requested;

    /// <inheritdoc/>
    public bool IsAllowedForRun(string method) => _allowedForRun.Contains(method);

    /// <summary>
    /// Starts an approval request for a held call. With no subscriber to <c>approval/requested</c> and no <see cref="Requested"/> handler, the call is denied at once.
    /// </summary>
    /// <param name="call">The call that the interceptor held.</param>
    /// <param name="reason">A sentence that tells the person why the approval is necessary.</param>
    /// <returns>The request, which is already answered when no one can answer it.</returns>
    /// <exception cref="InvalidOperationException">The server is not started.</exception>
    public IpcApproval Request(IpcHeldCall call, string reason)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(reason);
        var server = _server ?? throw new InvalidOperationException("The broker has no server. Start the server first.");
        var approval = new IpcApproval(++_nextId, call, reason);
        if (!server.Events.HasSubscribers(IpcEventNames.ApprovalRequested) && Requested is null)
        {
            Finish(approval, IpcApprovalAnswer.NoAnswerer, "The call is denied because no connection subscribes to approval/requested. Subscribe a client that can answer.");
            return approval;
        }

        _pending[approval.Id] = approval;
        approval.Timer = server.Loop.AddTimer(() => Finish(
            approval, IpcApprovalAnswer.TimedOut, $"The call is denied because no one answered within {Timeout.TotalSeconds:0.#} s."));
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
            Log.Error(exception, $"approval: observer failed");
        }

        return approval;
    }

    /// <summary>
    /// Answers a pending request. The first answer wins. The broker records the answer, then allows or denies the call, then raises <see cref="IpcApproval.Answered"/>.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="answer"><see cref="IpcApprovalAnswer.AllowOnce"/>, <see cref="IpcApprovalAnswer.AllowRun"/> or <see cref="IpcApprovalAnswer.Deny"/>.</param>
    /// <returns><see langword="false"/> when no pending request has the id.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="answer"/> is a timeout or a missing answerer.</exception>
    public bool Answer(long id, IpcApprovalAnswer answer)
    {
        if (answer is not (IpcApprovalAnswer.AllowOnce or IpcApprovalAnswer.AllowRun or IpcApprovalAnswer.Deny))
        {
            throw new ArgumentOutOfRangeException(nameof(answer), "The answer must be allow once, allow for the run, or deny.");
        }

        if (!_pending.TryGetValue(id, out var approval))
        {
            return false;
        }

        Finish(approval, answer, "A person denied the call.");
        return true;
    }

    internal void Attach(IpcServer server)
    {
        if (_server is not null && !ReferenceEquals(_server, server))
        {
            throw new InvalidOperationException("The broker already serves a server. A broker serves one server only.");
        }

        _server = server;
    }

    internal void Detach()
    {
        foreach (var approval in _pending.Values.ToArray())
        {
            Finish(approval, IpcApprovalAnswer.Deny, "The compositor stopped.");
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

        approval.Record(answer);
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
            approval.Finish();
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"approval: observer failed");
        }
    }
}
