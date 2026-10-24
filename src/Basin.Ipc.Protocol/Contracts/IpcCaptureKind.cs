namespace Basin.Ipc;

/// <summary>
/// The three targets of a capture.
/// </summary>
public enum IpcCaptureKind
{
    /// <summary>
    /// The reply carries the PNG as base64.
    /// </summary>
    Inline,
    /// <summary>
    /// The reply carries a memfd of raw pixels as a descriptor.
    /// </summary>
    Fd,
    /// <summary>
    /// The compositor writes a PNG file at an absolute path.
    /// </summary>
    Path,
}
