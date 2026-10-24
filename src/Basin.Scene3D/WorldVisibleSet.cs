namespace Basin.Scene3D;

/// <summary>
/// The result of a visibility walk: what one view can see. A consumer keeps one per view and clears it before each walk.
/// </summary>
public sealed class WorldVisibleSet
{
    internal readonly List<(WorldRoom Room, int Depth)> Queue = [];

    /// <summary>
    /// Gets the visible rooms, in the breadth-first order of the walk. The eye's room is first.
    /// </summary>
    public List<WorldRoom> Rooms { get; } = [];

    /// <summary>
    /// Gets the visible windows that are in a visible room, or in no room, and in the frustum.
    /// </summary>
    public List<WorldWindow> Windows { get; } = [];

    /// <summary>
    /// Gets the visible props.
    /// </summary>
    public List<WorldProp> Props { get; } = [];

    /// <summary>
    /// Gets the portals that have a target, are in a visible room and are in the frustum.
    /// </summary>
    public List<WorldPortal> Portals { get; } = [];

    /// <summary>
    /// Gets every surface on a layer of a visible window, each one time. A consumer sends their frame callbacks.
    /// </summary>
    public List<Surface> Surfaces { get; } = [];

    /// <summary>
    /// Gets the room that holds the eye, or the nearest room when no room holds it. It is <see langword="null"/> when the world has no room.
    /// </summary>
    public WorldRoom? EyeRoom { get; internal set; }

    /// <summary>
    /// Empties every list and sets <see cref="EyeRoom"/> to <see langword="null"/>. It keeps the list capacity, so a walk after it does not allocate.
    /// </summary>
    public void Clear()
    {
        Rooms.Clear();
        Windows.Clear();
        Props.Clear();
        Portals.Clear();
        Surfaces.Clear();
        Queue.Clear();
        EyeRoom = null;
    }

    /// <summary>
    /// Finds a surface in <see cref="Surfaces"/> by reference.
    /// </summary>
    /// <param name="surface">The surface to find.</param>
    /// <returns><see langword="true"/> when the walk found the surface on a visible window.</returns>
    public bool ContainsSurface(Surface surface)
    {
        for (var i = 0; i < Surfaces.Count; i++)
        {
            if (ReferenceEquals(Surfaces[i], surface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a window in <see cref="Windows"/> by reference.
    /// </summary>
    /// <param name="window">The window to find.</param>
    /// <returns><see langword="true"/> when the window is visible.</returns>
    public bool ContainsWindow(WorldWindow window)
    {
        for (var i = 0; i < Windows.Count; i++)
        {
            if (ReferenceEquals(Windows[i], window))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a room in <see cref="Rooms"/> by reference.
    /// </summary>
    /// <param name="room">The room to find.</param>
    /// <returns><see langword="true"/> when the room is visible.</returns>
    public bool ContainsRoom(WorldRoom room)
    {
        for (var i = 0; i < Rooms.Count; i++)
        {
            if (ReferenceEquals(Rooms[i], room))
            {
                return true;
            }
        }

        return false;
    }

    internal void AddSurface(Surface surface)
    {
        if (!ContainsSurface(surface))
        {
            Surfaces.Add(surface);
        }
    }
}
