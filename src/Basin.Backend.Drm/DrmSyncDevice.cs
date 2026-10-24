using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// Imports DRM syncobj timelines on one DRM device.
/// </summary>
public sealed class DrmSyncDevice : IDrmSyncDevice, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrmSyncDevice"/> class. It does not take ownership of the fd.
    /// </summary>
    /// <param name="drmFd">The DRM device fd.</param>
    public DrmSyncDevice(int drmFd) => DrmFd = drmFd;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrmSyncDevice"/> class on its own duplicate of a DRM fd. Imported timelines then stay valid after the caller closes its fd.
    /// </summary>
    /// <param name="drmFd">The DRM device fd to duplicate. The caller keeps it.</param>
    /// <returns>The device, which owns the duplicate and closes it at <see cref="Dispose"/>.</returns>
    /// <exception cref="InvalidOperationException">The fd cannot be duplicated.</exception>
    public static DrmSyncDevice Duplicate(int drmFd)
    {
        var own = fcntl(drmFd, FDupFdCloexec, 3);
        return own < 0
            ? throw new InvalidOperationException($"Cannot duplicate DRM fd {drmFd}.")
            : new DrmSyncDevice(own) { _ownsFd = true };
    }

    private const int FDupFdCloexec = 1030;
    private bool _ownsFd;

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    /// <summary>
    /// Closes the fd when the device owns it, from <see cref="Duplicate"/>. Otherwise it does nothing. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_ownsFd)
        {
            _ownsFd = false;
            _ = close(DrmFd);
        }
    }

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
    /// Gets the DRM device fd. The caller keeps ownership of it, except for a device from <see cref="Duplicate"/>.
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
