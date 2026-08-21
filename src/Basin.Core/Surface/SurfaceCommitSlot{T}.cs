namespace Basin;

public static class SurfaceCommitSlot<T>
    where T : class, IDisposable
{
    public static readonly int Index = SurfaceCommitSlots.Reserve();
}
