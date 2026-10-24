namespace Basin.WindowManager;

/// <summary>
/// The readiness of an fd, as a watch asks for it and as the poll reports it. Combine values to name more than one condition.
/// </summary>
[Flags]
public enum WmFdReadiness : uint
{
    /// <summary>No condition.</summary>
    None = 0,

    /// <summary>The fd has data to read.</summary>
    Readable = 1,

    /// <summary>The fd can take a write without blocking.</summary>
    Writable = 2,

    /// <summary>The peer closed the connection. The poll reports it without a request.</summary>
    Hangup = 4,

    /// <summary>The fd is in an error state. The poll reports it without a request.</summary>
    Error = 8,
}
