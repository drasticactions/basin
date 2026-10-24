namespace Basin;

/// <summary>
/// How the managed <c>wl_shm</c> reads a client pool that the client can shrink.
/// </summary>
public enum ShmAccessPolicy
{
    /// <summary>Renderers read the client mapping directly.</summary>
    Direct,

    /// <summary>Each commit copies the damaged rows into a compositor-owned shadow with a fault-safe copy, and renderers read the shadow.</summary>
    Guarded,
}
