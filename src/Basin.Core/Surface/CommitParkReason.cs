namespace Basin;

[Flags]
public enum CommitParkReason
{
    None = 0,

    SubsurfaceSync = 1,

    FifoBarrier = 2,

    CommitTiming = 4,

    Held = 8,
}
