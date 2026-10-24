namespace Basin.Scene3D.Scene;

/// <summary>
/// Frame pacing for the surfaces in a world. The windows' scene nodes are in a disabled layer, so the scene cannot send their frame callbacks.
/// </summary>
public sealed class WorldFrameCallbacks
{
    private long _lastFloor;

    /// <summary>
    /// Gets the shortest interval, in nanoseconds, between two times that <see cref="FloorDue"/> answers <see langword="true"/>. The default is one second.
    /// </summary>
    public long FloorNanos { get; init; } = 1_000_000_000;

    /// <summary>
    /// Sends <c>wl_surface.frame</c> done to every surface that a visibility walk found.
    /// </summary>
    /// <param name="visible">The visible set of the output that presented.</param>
    /// <param name="timeMs">The callback time in milliseconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visible"/> is <see langword="null"/>.</exception>
    public static void SendVisible(WorldVisibleSet visible, uint timeMs)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var surfaces = visible.Surfaces;
        for (var i = 0; i < surfaces.Count; i++)
        {
            surfaces[i].SendFrameDone(timeMs);
        }
    }

    /// <summary>
    /// Adds every surface that a visibility walk found to a presented set.
    /// </summary>
    /// <param name="set">The set that receives the surfaces.</param>
    /// <param name="visible">The visible set of the output that presented.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> or <paramref name="visible"/> is <see langword="null"/>.</exception>
    public static void AddPresented(PresentedSet set, WorldVisibleSet visible)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(visible);
        var surfaces = visible.Surfaces;
        for (var i = 0; i < surfaces.Count; i++)
        {
            set.Add(surfaces[i]);
        }
    }

    /// <summary>
    /// Answers <see langword="true"/> at most once in each <see cref="FloorNanos"/>. The consumer then sends frame callbacks to the windows that no output sees, so they are throttled but not starved.
    /// </summary>
    /// <param name="nowNanos">The current time in nanoseconds.</param>
    /// <returns><see langword="true"/> when more than <see cref="FloorNanos"/> has passed since the last <see langword="true"/>.</returns>
    public bool FloorDue(long nowNanos)
    {
        if (nowNanos - _lastFloor <= FloorNanos)
        {
            return false;
        }

        _lastFloor = nowNanos;
        return true;
    }

    /// <summary>
    /// Adds every surface that a visibility walk found to a presence list, each against the whole output box.
    /// </summary>
    /// <param name="boxes">The presence list that receives the entries.</param>
    /// <param name="visible">The visible set of the output.</param>
    /// <param name="output">The output's box in layout pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="boxes"/> or <paramref name="visible"/> is <see langword="null"/>.</exception>
    public static void AddPresence(List<SurfaceBox> boxes, WorldVisibleSet visible, Box output)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(visible);
        var surfaces = visible.Surfaces;
        for (var i = 0; i < surfaces.Count; i++)
        {
            boxes.Add(new SurfaceBox(surfaces[i], output));
        }
    }
}
