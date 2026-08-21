namespace Basin.Capabilities;

public interface IDrmSyncDevice
{
    int DrmFd { get; }

    bool TryImportTimeline(int syncobjFd, out DrmSyncobjTimeline timeline);
}
