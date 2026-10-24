namespace Basin.Ipc;

/// <summary>
/// The three kinds of message that the server writes.
/// </summary>
public enum IpcMessageKind
{
    /// <summary>
    /// A successful reply. It carries <c>result</c>.
    /// </summary>
    Result,
    /// <summary>
    /// An error reply. It carries <c>error</c> with a code and a message.
    /// </summary>
    Error,
    /// <summary>
    /// An event. It carries the <c>event</c> name and its <c>data</c>.
    /// </summary>
    Event,
}
