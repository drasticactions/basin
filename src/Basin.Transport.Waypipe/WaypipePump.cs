namespace Basin.Transport.Waypipe;

/// <summary>
/// Selects how a <see cref="WaypipeChannel"/> reads its stream. The choice is made once at attach.
/// </summary>
public enum WaypipePump
{
    /// <summary>
    /// A blocking read loop on a background thread. Writes go to the stream directly under a lock.
    /// </summary>
    Thread,

    /// <summary>
    /// An asynchronous read loop on the synchronization context of the attaching thread. Writes are queued and one writer drains them. Use it where the host cannot own a thread.
    /// </summary>
    Async,
}
