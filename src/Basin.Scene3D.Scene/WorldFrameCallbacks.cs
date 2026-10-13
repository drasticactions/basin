namespace Basin.Scene3D.Scene;

public sealed class WorldFrameCallbacks
{
    private long _lastFloor;

    public long FloorNanos { get; init; } = 1_000_000_000;

    public static void SendVisible(WorldVisibleSet visible, uint timeMs)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var surfaces = visible.Surfaces;
        for (var i = 0; i < surfaces.Count; i++)
        {
            surfaces[i].SendFrameDone(timeMs);
        }
    }

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

    public bool FloorDue(long nowNanos)
    {
        if (nowNanos - _lastFloor <= FloorNanos)
        {
            return false;
        }

        _lastFloor = nowNanos;
        return true;
    }

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
