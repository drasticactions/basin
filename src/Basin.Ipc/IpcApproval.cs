namespace Basin.Ipc;

/// <summary>
/// One approval request for a held call, from its start to its answer.
/// </summary>
public sealed class IpcApproval
{
    internal IpcApproval(long id, IpcHeldCall call, string reason)
    {
        Id = id;
        Call = call;
        Method = call.Method;
        Arguments = call.Parameters.ToArray();
        Reason = reason;
    }

    /// <summary>
    /// Gets the request id, unique for the broker.
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// Gets the name of the held method.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets a copy of the held call's params as raw JSON.
    /// </summary>
    public ReadOnlyMemory<byte> Arguments { get; }

    /// <summary>
    /// Gets the sentence that the compositor wrote to explain the request.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the answer, or <see langword="null"/> while the request waits. The broker sets it before it runs or refuses the call.
    /// </summary>
    public IpcApprovalAnswer? Answer { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the request has an answer.
    /// </summary>
    public bool IsAnswered => Answer is not null;

    /// <summary>
    /// Occurs once, after the broker records the answer and allows or denies the call.
    /// </summary>
    public event Action<IpcApproval>? Answered;

    internal IpcHeldCall Call { get; }

    internal IEventSource? Timer { get; set; }

    internal void Record(IpcApprovalAnswer answer) => Answer = answer;

    internal void Finish() => Answered?.Invoke(this);
}
