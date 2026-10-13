using System.Runtime.Versioning;

namespace Basin.Desktop;

[SupportedOSPlatform("linux")]
public sealed class SyncobjCommitFence : ISurfaceCommitFence
{
    private DrmSyncobjTimeline.DrmSyncobjWaiter? _waiter;

    private SyncobjCommitFence()
    {
    }

    public bool Signaled { get; private set; }

    public event Action<ISurfaceCommitFence>? SignaledChanged;

    public static SyncobjCommitFence? TryCreate(ICompositorEventLoop loop, DrmSyncobjTimeline timeline, ulong point)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(timeline);
        var fence = new SyncobjCommitFence();
        fence._waiter = timeline.TryWait(loop, point, fence.OnReady);
        return fence._waiter is null ? null : fence;
    }

    public void Dispose()
    {
        _waiter?.Dispose();
        _waiter = null;
    }

    private void OnReady()
    {
        if (Signaled)
        {
            return;
        }

        Dispose();
        Signaled = true;
        SignaledChanged?.Invoke(this);
    }
}
