using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// The 3D world that a compositor draws its windows in. The consumer owns every list and changes them on the compositor thread.
/// </summary>
public sealed class World
{
    /// <summary>
    /// Gets the rooms and corridors. The visibility walk starts in one of them.
    /// </summary>
    public List<WorldRoom> Rooms { get; } = [];

    /// <summary>
    /// Gets every window in the world. The visibility walk and the picker read this list.
    /// </summary>
    public List<WorldWindow> Windows { get; } = [];

    /// <summary>
    /// Gets every portal in the world. The walk finds visible portals through <see cref="WorldRoom.Portals"/> instead.
    /// </summary>
    public List<WorldPortal> Portals { get; } = [];

    /// <summary>
    /// Gets every prop in the world.
    /// </summary>
    public List<WorldProp> Props { get; } = [];

    /// <summary>
    /// Gets or sets the skinned avatar. A pass draws it only in a view through a portal, so a camera inside it does not see its inside faces.
    /// </summary>
    public WorldAvatar? Avatar { get; set; }

    /// <summary>
    /// Gets or sets the linear RGB color that a pass clears to and shows where no geometry is.
    /// </summary>
    public Vector3 ClearColor { get; set; } = new(0.02f, 0.02f, 0.03f);

    /// <summary>
    /// Gets or sets the linear RGB ambient light that the lit shaders add to every surface.
    /// </summary>
    public Vector3 Ambient { get; set; } = new(0.18f, 0.17f, 0.2f);

    /// <summary>
    /// Finds the first room in <see cref="Rooms"/> whose bounds hold a point.
    /// </summary>
    /// <param name="point">The point in world space.</param>
    /// <returns>The room, or <see langword="null"/> when no room holds the point.</returns>
    public WorldRoom? RoomAt(Vector3 point)
    {
        for (var i = 0; i < Rooms.Count; i++)
        {
            if (Rooms[i].Contains(point))
            {
                return Rooms[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the room whose bounds are nearest to a point. A room that holds the point is at distance 0.
    /// </summary>
    /// <param name="point">The point in world space.</param>
    /// <returns>The nearest room, or <see langword="null"/> when the world has no room.</returns>
    public WorldRoom? NearestRoom(Vector3 point)
    {
        WorldRoom? best = null;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < Rooms.Count; i++)
        {
            var bounds = Rooms[i].Bounds;
            var clamped = Vector3.Clamp(point, bounds.Min, bounds.Max);
            var distance = Vector3.DistanceSquared(clamped, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = Rooms[i];
            }
        }

        return best;
    }
}
