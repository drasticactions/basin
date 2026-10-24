namespace Basin;

/// <summary>
/// The readiness of a file descriptor. The values match <c>WL_EVENT_READABLE</c> and its siblings, and a combination means each named condition.
/// </summary>
[Flags]
public enum FdReadiness : uint
{
    /// <summary>
    /// No condition.
    /// </summary>
    None = 0,
    /// <summary>
    /// The descriptor has data to read.
    /// </summary>
    Readable = 1,
    /// <summary>
    /// The descriptor accepts a write.
    /// </summary>
    Writable = 2,
    /// <summary>
    /// The peer closed the connection. The loop always reports it, whatever the watch mask.
    /// </summary>
    Hangup = 4,
    /// <summary>
    /// An error condition. The loop always reports it, whatever the watch mask.
    /// </summary>
    Error = 8,
}
