using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A vertical opening that joins two rooms. The visibility walk passes through it only when it is in the frustum.
/// </summary>
public sealed class WorldDoorway
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldDoorway"/> class. Add it to the <see cref="WorldRoom.Doorways"/> of both rooms so the walk can go each way.
    /// </summary>
    /// <param name="from">One room that the doorway joins.</param>
    /// <param name="to">The other room.</param>
    /// <param name="center">The center of the opening in world space.</param>
    /// <param name="normal">The direction through the opening. It must not be vertical. The constructor normalizes it.</param>
    /// <param name="width">The width of the opening in meters.</param>
    /// <param name="height">The height of the opening in meters.</param>
    public WorldDoorway(WorldRoom from, WorldRoom to, Vector3 center, Vector3 normal, float width, float height)
    {
        From = from;
        To = to;
        Center = center;
        Normal = Vector3.Normalize(normal);
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets one room that the doorway joins.
    /// </summary>
    public WorldRoom From { get; }

    /// <summary>
    /// Gets the other room that the doorway joins.
    /// </summary>
    public WorldRoom To { get; }

    /// <summary>
    /// Gets the center of the opening in world space.
    /// </summary>
    public Vector3 Center { get; }

    /// <summary>
    /// Gets the unit direction through the opening.
    /// </summary>
    public Vector3 Normal { get; }

    /// <summary>
    /// Gets the width of the opening in meters.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the height of the opening in meters.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the world-space box round the opening's rectangle. The visibility walk culls the doorway by it.
    /// </summary>
    public WorldBounds Bounds
    {
        get
        {
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Normal)) * (Width * 0.5f);
            var up = Vector3.UnitY * (Height * 0.5f);
            return WorldBounds.Empty
                .Include(Center - side - up)
                .Include(Center + side + up)
                .Include(Center - side + up)
                .Include(Center + side - up);
        }
    }
}
