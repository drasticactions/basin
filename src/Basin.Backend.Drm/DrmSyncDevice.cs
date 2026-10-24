using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// Imports DRM syncobj timelines on one DRM device.
/// </summary>
public sealed class DrmSyncDevice : IDrmSyncDevice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrmSyncDevice"/> class. It does not take ownership of the fd.
    /// </summary>
    /// <param name="drmFd">The DRM device fd.</param>
    public DrmSyncDevice(int drmFd) => DrmFd = drmFd;

    /// <summary>
    /// Tests whether a DRM device supports timeline syncobjs.
    /// </summary>
    /// <param name="drmFd">The DRM device fd. The call does not close it.</param>
    /// <returns><see langword="true"/> when the device reports the timeline capability. It is <see langword="false"/> for a negative fd.</returns>
    public static bool SupportsTimelines(int drmFd)
    {
        if (drmFd < 0)
        {
            return false;
        }

        using var device = DrmDevice.FromFd(drmFd, ownsFd: false);
        return device.TryGetCapability(DrmCapability.SyncObjTimeline, out var timelines) && timelines != 0;
    }

    /// <summary>
    /// Gets the DRM device fd. The caller keeps ownership of it.
    /// </summary>
    public int DrmFd { get; }

    /// <summary>
    /// Imports a syncobj fd as a timeline on this device.
    /// </summary>
    /// <param name="syncobjFd">The syncobj fd from the client.</param>
    /// <param name="timeline">The imported timeline. It is not valid when the call fails.</param>
    /// <returns><see langword="false"/> when the kernel refuses the fd.</returns>
    public bool TryImportTimeline(int syncobjFd, out DrmSyncobjTimeline timeline)
    {
        try
        {
            timeline = DrmSyncobjTimeline.ImportFd(DrmFd, syncobjFd);
            return true;
        }
        catch (InvalidOperationException)
        {
            timeline = null!;
            return false;
        }
    }
}
