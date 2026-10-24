namespace Basin;

/// <summary>
/// Identifies the DRM device that a renderer draws on.
/// </summary>
public interface IRenderDevice
{
    /// <summary>
    /// Gets the fd of the DRM node. The device owns it.
    /// </summary>
    int DrmFd { get; }

    /// <summary>
    /// Gets the path of the DRM node, for example a render node.
    /// </summary>
    string DevicePath { get; }
}
