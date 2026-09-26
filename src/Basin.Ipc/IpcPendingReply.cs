namespace Basin.Ipc;

public sealed class IpcPendingReply : IpcReply
{
    internal IpcPendingReply(IIpcReplySink sink, ReadOnlySpan<byte> id, string method, object? frontState)
        : base(sink)
    {
        Begin(id, method);
        FrontState = frontState;
    }

    public bool IsOpen => !IsDone && Sink.IsOpen;

    public bool IsDone { get; private set; }

    public bool Completed { get; private set; }

    public bool Complete()
    {
        if (IsDone)
        {
            throw new InvalidOperationException("a pending reply completes once");
        }

        IsDone = true;
        if (!Sink.IsOpen)
        {
            ReleaseFds();
            return false;
        }

        if (!ResultIsBalanced)
        {
            Fail(IpcErrorCodes.Internal, "the handler left its result unfinished");
        }

        Sink.Deliver(this);
        Completed = true;
        return true;
    }
}
