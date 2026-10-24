using System.Runtime.Versioning;

namespace Basin.Desktop;

/// <summary>
/// Holds a surface commit until a point on a DRM syncobj timeline signals. The wait runs on the compositor loop.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SyncobjCommitFence : ISurfaceCommitFence
{
    private DrmSyncobjTimeline.DrmSyncobjWaiter? _waiter;

    private SyncobjCommitFence()
    {
    }

    /// <inheritdoc/>
    public bool Signaled { get; private set; }

    /// <summary>
    /// Occurs once, on the loop thread, when the point signals.
    /// </summary>
    public event Action<ISurfaceCommitFence>? SignaledChanged;

    /// <summary>
    /// Creates a fence that waits on the loop for a timeline point.
    /// </summary>
    /// <param name="loop">The loop that runs the wait.</param>
    /// <param name="timeline">The timeline to wait on.</param>
    /// <param name="point">The point to wait for.</param>
    /// <returns>The fence, or <see langword="null"/> when the timeline cannot wait on the loop.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="timeline"/> is <see langword="null"/>.</exception>
    public static SyncobjCommitFence? TryCreate(ICompositorEventLoop loop, DrmSyncobjTimeline timeline, ulong point)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(timeline);
        var fence = new SyncobjCommitFence();
        fence._waiter = timeline.TryWait(loop, point, fence.OnReady);
        return fence._waiter is null ? null : fence;
    }

    /// <summary>
    /// Stops the wait. <see cref="SignaledChanged"/> does not occur after this call.
    /// </summary>
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
