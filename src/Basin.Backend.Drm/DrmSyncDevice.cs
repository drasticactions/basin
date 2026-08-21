using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

public sealed class DrmSyncDevice : IDrmSyncDevice
{
    public DrmSyncDevice(int drmFd) => DrmFd = drmFd;

    public static bool SupportsTimelines(int drmFd)
    {
        if (drmFd < 0)
        {
            return false;
        }

        using var device = DrmDevice.FromFd(drmFd, ownsFd: false);
        return device.TryGetCapability(DrmCapability.SyncObjTimeline, out var timelines) && timelines != 0;
    }

    public int DrmFd { get; }

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
