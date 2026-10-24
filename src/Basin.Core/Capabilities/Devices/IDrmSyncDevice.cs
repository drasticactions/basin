namespace Basin.Capabilities;

/// <summary>
/// A DRM device that can import syncobj timelines for explicit synchronization.
/// </summary>
public interface IDrmSyncDevice
{
    /// <summary>
    /// Gets the DRM fd that timelines are imported on. The device owns it.
    /// </summary>
    int DrmFd { get; }

    /// <summary>
    /// Imports a syncobj fd that a client sent as a timeline.
    /// </summary>
    /// <param name="syncobjFd">The syncobj fd. The caller keeps ownership of it.</param>
    /// <param name="timeline">The imported timeline on success. It is not valid on failure.</param>
    /// <returns><see langword="true"/> when the kernel accepted the fd, otherwise <see langword="false"/>.</returns>
    bool TryImportTimeline(int syncobjFd, out DrmSyncobjTimeline timeline);
}
