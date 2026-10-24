namespace Basin;

/// <summary>
/// Hands out the process-wide indexes of <see cref="SurfaceState"/> extension slots.
/// </summary>
public static class SurfaceCommitSlots
{
    private static int _next;

    /// <summary>Gets the number of slots reserved so far.</summary>
    public static int Count => _next;

    /// <summary>Reserves the next slot. Each call returns a new index, and an index is never returned again.</summary>
    /// <returns>The zero-based slot index.</returns>
    public static int Reserve() => _next++;
}
