namespace Basin.Scene3D;

/// <summary>
/// The visibility walk over the doorway graph.
/// </summary>
public static class WorldVisibility
{
    /// <summary>
    /// Walks the doorways breadth first from the eye's room and adds what <paramref name="view"/> can see to <paramref name="into"/>.
    /// It does not clear <paramref name="into"/>, and it does not allocate after the lists have grown.
    /// </summary>
    /// <param name="world">The world to walk.</param>
    /// <param name="view">The view that culls rooms, doorways, portals, windows and props.</param>
    /// <param name="roomDepth">The number of doorways that the walk can pass through from the eye's room.</param>
    /// <param name="into">The set that receives the result. It keeps an earlier <see cref="WorldVisibleSet.EyeRoom"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="into"/> is <see langword="null"/>.</exception>
    public static void Collect(World world, in WorldView view, int roomDepth, WorldVisibleSet into)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(into);
        var frustum = view.Frustum();
        var eye = view.Eye;
        var start = world.RoomAt(eye) ?? world.NearestRoom(eye);
        into.EyeRoom ??= start;
        into.Queue.Clear();
        if (start is not null)
        {
            Visit(start, 0);
        }

        for (var head = 0; head < into.Queue.Count; head++)
        {
            var (room, depth) = into.Queue[head];
            if (depth >= roomDepth)
            {
                continue;
            }

            var doorways = room.Doorways;
            for (var i = 0; i < doorways.Count; i++)
            {
                var doorway = doorways[i];
                var next = ReferenceEquals(doorway.From, room) ? doorway.To : doorway.From;
                if (frustum.Intersects(doorway.Bounds) && frustum.Intersects(next.Bounds))
                {
                    Visit(next, depth + 1);
                }
            }
        }

        var windows = world.Windows;
        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            if (!window.Visible || into.ContainsWindow(window) ||
                (window.Room is { } home && !into.ContainsRoom(home)) ||
                !frustum.Intersects(window.Bounds))
            {
                continue;
            }

            into.Windows.Add(window);
            var layers = window.Layers;
            for (var l = 0; l < layers.Length; l++)
            {
                if (layers[l].Surface is { } surface)
                {
                    into.AddSurface(surface);
                }
            }
        }

        var props = world.Props;
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            if ((prop.Room is null || into.ContainsRoom(prop.Room)) && !into.Props.Contains(prop) &&
                frustum.Intersects(prop.Bounds))
            {
                into.Props.Add(prop);
            }
        }

        return;

        void Visit(WorldRoom room, int depth)
        {
            if (into.ContainsRoom(room))
            {
                return;
            }

            into.Rooms.Add(room);
            into.Queue.Add((room, depth));
            var portals = room.Portals;
            for (var i = 0; i < portals.Count; i++)
            {
                if (portals[i].Target is not null && frustum.Intersects(portals[i].Bounds) && !into.Portals.Contains(portals[i]))
                {
                    into.Portals.Add(portals[i]);
                }
            }
        }
    }
}
