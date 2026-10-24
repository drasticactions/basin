using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A rectangle on a wall that shows the view behind its <see cref="Target"/>. Its local +z is its front.
/// </summary>
public sealed class WorldPortal
{
    private static readonly Matrix4x4 HalfTurn = Matrix4x4.CreateRotationY(MathF.PI);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldPortal"/> class. Add it to <see cref="WorldRoom.Portals"/> so the walk can find it.
    /// </summary>
    /// <param name="room">The room whose wall holds the portal.</param>
    /// <param name="center">The center of the rectangle in world space.</param>
    /// <param name="yaw">The turn round the world y axis, in radians. At 0 the front faces world +z.</param>
    /// <param name="width">The width in meters.</param>
    /// <param name="height">The height in meters.</param>
    public WorldPortal(WorldRoom room, Vector3 center, float yaw, float width, float height)
    {
        Room = room;
        Center = center;
        Yaw = yaw;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the room whose wall holds the portal.
    /// </summary>
    public WorldRoom Room { get; }

    /// <summary>
    /// Gets the center of the rectangle in world space.
    /// </summary>
    public Vector3 Center { get; }

    /// <summary>
    /// Gets the turn round the world y axis, in radians.
    /// </summary>
    public float Yaw { get; }

    /// <summary>
    /// Gets the width in meters.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the height in meters.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets or sets the portal that this one shows the far side of. A portal with no target is not collected or drawn.
    /// </summary>
    public WorldPortal? Target { get; set; }

    /// <summary>
    /// Gets the model matrix from the portal's local space to world space.
    /// </summary>
    public Matrix4x4 Frame => Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateTranslation(Center);

    /// <summary>
    /// Gets the unit front normal in world space.
    /// </summary>
    public Vector3 Normal => Vector3.TransformNormal(Vector3.UnitZ, Frame);

    /// <summary>
    /// Gets the plane of the portal in world space. Points in front of the portal give a positive distance.
    /// </summary>
    public Vector4 Plane
    {
        get
        {
            var normal = Normal;
            return new Vector4(normal, -Vector3.Dot(normal, Center));
        }
    }

    /// <summary>
    /// Builds the transfer that maps the space in front of this portal to the space behind <paramref name="target"/>. It turns a half turn on the way.
    /// </summary>
    /// <param name="target">The portal to step out of.</param>
    /// <returns>The world-to-world transform for <see cref="WorldView.Through"/>.</returns>
    public Matrix4x4 TransferTo(WorldPortal target)
    {
        Matrix4x4.Invert(Frame, out var inverse);
        return inverse * HalfTurn * target.Frame;
    }

    /// <summary>
    /// Writes the four world-space corners of the rectangle: bottom left, bottom right, top right, top left, in local space.
    /// </summary>
    /// <param name="corners">A span of at least four points.</param>
    public void Corners(Span<Vector3> corners)
    {
        var frame = Frame;
        var halfWidth = Width * 0.5f;
        var halfHeight = Height * 0.5f;
        corners[0] = Vector3.Transform(new Vector3(-halfWidth, -halfHeight, 0), frame);
        corners[1] = Vector3.Transform(new Vector3(halfWidth, -halfHeight, 0), frame);
        corners[2] = Vector3.Transform(new Vector3(halfWidth, halfHeight, 0), frame);
        corners[3] = Vector3.Transform(new Vector3(-halfWidth, halfHeight, 0), frame);
    }

    /// <summary>
    /// Gets the world-space box round the four corners. The visibility walk culls the portal by it.
    /// </summary>
    public WorldBounds Bounds
    {
        get
        {
            Span<Vector3> corners = stackalloc Vector3[4];
            Corners(corners);
            var bounds = WorldBounds.Empty;
            foreach (var corner in corners)
            {
                bounds = bounds.Include(corner);
            }

            return bounds;
        }
    }
}
