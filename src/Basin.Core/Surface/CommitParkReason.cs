namespace Basin;

/// <summary>
/// The reasons that a surface holds a commit before it applies. A commit can have several reasons, and it applies only when each one is cleared.
/// </summary>
[Flags]
public enum CommitParkReason
{
    /// <summary>The commit is not held.</summary>
    None = 0,

    /// <summary>The surface is a synchronized subsurface. The parent's next commit applies the state.</summary>
    SubsurfaceSync = 1,

    /// <summary>The commit waits on an armed fifo barrier until a refresh cycle completes.</summary>
    FifoBarrier = 2,

    /// <summary>The commit carries a presentation time that is still in the future.</summary>
    CommitTiming = 4,

    /// <summary>The compositor asked to hold the commit through <see cref="Surface.HoldNextCommit"/>. Only <see cref="Surface.ReleaseHeldCommits"/> clears it.</summary>
    Held = 8,

    /// <summary>The commit waits for an acquire fence, explicit or implicit, to signal.</summary>
    Fence = 16,
}
