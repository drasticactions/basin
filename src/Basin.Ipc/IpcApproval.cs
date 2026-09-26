namespace Basin.Ipc;

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

    public long Id { get; }

    public string Method { get; }

    public ReadOnlyMemory<byte> Arguments { get; }

    public string Reason { get; }

    public IpcApprovalAnswer? Answer { get; private set; }

    public bool IsAnswered => Answer is not null;

    public event Action<IpcApproval>? Answered;

    internal IpcHeldCall Call { get; }

    internal IEventSource? Timer { get; set; }

    internal void Finish(IpcApprovalAnswer answer)
    {
        Answer = answer;
        Answered?.Invoke(this);
    }
}
