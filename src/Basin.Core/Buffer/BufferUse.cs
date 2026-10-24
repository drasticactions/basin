namespace Basin;

/// <summary>
/// The uses that an <see cref="IAllocator"/> must make a new buffer fit for. Combine the flags to ask for more than one use.
/// </summary>
[Flags]
public enum BufferUse
{
    /// <summary>A renderer draws into the buffer or samples it.</summary>
    Render = 1,
    /// <summary>A display plane scans the buffer out directly.</summary>
    Scanout = 2,
    /// <summary>A hardware cursor plane shows the buffer.</summary>
    Cursor = 4,
}
