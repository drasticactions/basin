namespace Basin;

/// <summary>
/// The extension slot that one payload type uses on every <see cref="SurfaceState"/> in the process.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public static class SurfaceCommitSlot<T>
    where T : class, IDisposable
{
    /// <summary>The slot index of <typeparamref name="T"/>. It is reserved the first time the type is used.</summary>
    public static readonly int Index = SurfaceCommitSlots.Reserve();
}
