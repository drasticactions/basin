namespace Basin.Ipc;

/// <summary>
/// A reply that a handler deferred through <see cref="IpcReply.Defer"/> and completes later on the compositor thread.
/// </summary>
public sealed class IpcPendingReply : IpcReply
{
    internal IpcPendingReply(IIpcReplySink sink, ReadOnlySpan<byte> id, string method, object? frontState)
        : base(sink)
    {
        Begin(id, method);
        FrontState = frontState;
    }

    /// <summary>
    /// Gets a value indicating whether the reply is not done and its sink can still take it.
    /// </summary>
    public bool IsOpen => !IsDone && Sink.IsOpen;

    /// <summary>
    /// Gets a value indicating whether <see cref="Complete"/> ran.
    /// </summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Complete"/> delivered the reply to an open sink.
    /// </summary>
    public bool Completed { get; private set; }

    internal bool Rerun { get; set; }

    /// <summary>
    /// Finishes the reply and sends it. A result left unfinished becomes an <c>internal</c> error. A reply whose sink closed is dropped and its descriptors are closed.
    /// </summary>
    /// <returns><see langword="true"/> when the sink took the reply, <see langword="false"/> when its connection had closed.</returns>
    /// <exception cref="InvalidOperationException">The reply completed before.</exception>
    public bool Complete()
    {
        if (IsDone)
        {
            throw new InvalidOperationException("The pending reply is already complete. Complete it once only.");
        }

        IsDone = true;
        if (!ResultIsBalanced)
        {
            Fail(IpcErrorCodes.Internal, "The handler did not finish its result.");
        }

        if (Intercepted is { } server)
        {
            Intercepted = null;
            server.After(Method, this, CallStarted);
        }

        if (!Sink.IsOpen)
        {
            ReleaseFds();
            return false;
        }

        Sink.Deliver(this);
        Completed = true;
        return true;
    }
}
