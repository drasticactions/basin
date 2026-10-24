namespace Basin.Ipc;

/// <summary>
/// Receives finished replies. A socket connection and the stdin line front are the two sinks.
/// </summary>
public interface IIpcReplySink
{
    /// <summary>
    /// Gets a value indicating whether the sink can still take a reply. It is <see langword="false"/> after its connection closes.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Gets the state that the sink keeps for its client, such as the resources that the client owns.
    /// </summary>
    IpcClientState State { get; }

    /// <summary>
    /// Sends a finished reply to the client.
    /// </summary>
    /// <param name="reply">The reply. The sink takes or closes the descriptors that it carries.</param>
    void Deliver(IpcReply reply);
}
