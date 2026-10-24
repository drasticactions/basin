using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A picture frame round a <see cref="WorldWindow"/>, with an optional plaque below it. Its sizes are in meters in the window's local space.
/// </summary>
public sealed class WorldWindowFrame
{
    /// <summary>
    /// Gets or sets a value indicating whether the frame draws and picks. A hidden frame acts as if the window had no frame.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets or sets the width of each rail in meters. The outer size is the canvas plus two rails.
    /// </summary>
    public float Rail { get; set; } = 0.06f;

    /// <summary>
    /// Gets or sets the distance in meters from the front plane back to the wall.
    /// </summary>
    public float Depth { get; set; } = 0.04f;

    /// <summary>
    /// Gets or sets the distance in meters that the canvas sits behind the front plane.
    /// </summary>
    public float Recess { get; set; } = 0.015f;

    /// <summary>
    /// Gets or sets the fraction of the rail width, from the canvas outward, that picks as an edge. The rest picks as <see cref="WorldFramePart.Face"/>.
    /// </summary>
    public float EdgeShare { get; set; } = 0.4f;

    /// <summary>
    /// Gets or sets the frame mesh in the window's local space, from <see cref="WorldPrimitives.Moulding"/>. With no mesh, a pass draws only the back panel and the plaque.
    /// </summary>
    public WorldMesh? Moulding { get; set; }

    /// <summary>
    /// Gets or sets the material of the moulding. Focus and flash change the albedo when it is drawn.
    /// </summary>
    public WorldMaterial Material { get; set; } = WorldMaterial.Default;

    /// <summary>
    /// Gets or sets the plaque width in meters. The frame has no plaque unless the width and the height are positive.
    /// </summary>
    public float PlaqueWidth { get; set; }

    /// <summary>
    /// Gets or sets the plaque height in meters.
    /// </summary>
    public float PlaqueHeight { get; set; }

    /// <summary>
    /// Gets or sets the gap in meters between the bottom rail and the plaque.
    /// </summary>
    public float PlaqueGap { get; set; } = 0.012f;

    /// <summary>
    /// Gets or sets how far the plaque stands off the wall, in meters.
    /// </summary>
    public float PlaqueThickness { get; set; } = 0.008f;

    /// <summary>
    /// Gets or sets the material of the plaque body, and of its face when it has no texture.
    /// </summary>
    public WorldMaterial PlaqueMaterial { get; set; } = WorldMaterial.Default;

    /// <summary>
    /// Gets or sets the texture on the plaque's face. A dmabuf texture takes part in the pass's fences.
    /// </summary>
    public ITexture? PlaqueTexture { get; set; }

    /// <summary>
    /// Gets or sets the part of <see cref="PlaqueTexture"/> to sample, in buffer pixels. An empty box samples the whole texture.
    /// </summary>
    public FBox PlaqueSource { get; set; }

    /// <summary>
    /// Gets or sets the width of the plaque in plaque pixels. A plaque hit gives its x coordinate in this range.
    /// </summary>
    public int PlaquePixelWidth { get; set; }

    /// <summary>
    /// Gets or sets the height of the plaque in plaque pixels.
    /// </summary>
    public int PlaquePixelHeight { get; set; }

    /// <summary>
    /// Gets a value indicating whether the plaque width and height are both positive.
    /// </summary>
    public bool HasPlaque => PlaqueWidth > 0 && PlaqueHeight > 0;

    /// <summary>
    /// Makes a shallow copy. The copy shares the moulding mesh and the plaque texture.
    /// </summary>
    /// <returns>A new frame with the same values.</returns>
    public WorldWindowFrame Clone() => (WorldWindowFrame)MemberwiseClone();

    /// <summary>
    /// Gives the size of the frame round a canvas.
    /// </summary>
    /// <param name="innerWidth">The canvas width in meters.</param>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The canvas size plus two rails on each axis.</returns>
    public Vector2 OuterSize(float innerWidth, float innerHeight) =>
        new(innerWidth + (2 * Rail), innerHeight + (2 * Rail));

    /// <summary>
    /// Gives the local-space box of the frame, from the front plane back to the wall.
    /// </summary>
    /// <param name="innerWidth">The canvas width in meters.</param>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The box, centered on the canvas.</returns>
    public WorldBounds OuterBox(float innerWidth, float innerHeight)
    {
        var half = OuterSize(innerWidth, innerHeight) * 0.5f;
        return new WorldBounds(new Vector3(-half.X, -half.Y, -Depth), new Vector3(half.X, half.Y, 0));
    }

    /// <summary>
    /// Gives the local-space center of the plaque's front face.
    /// </summary>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The point under the bottom rail, <see cref="PlaqueThickness"/> off the wall.</returns>
    public Vector3 PlaqueCenter(float innerHeight) =>
        new(0, -((innerHeight * 0.5f) + Rail + PlaqueGap + (PlaqueHeight * 0.5f)), PlaqueThickness - Depth);

    /// <summary>
    /// Gives the local-space box of the plaque, from its face back to the wall.
    /// </summary>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The plaque box. It is meaningless when <see cref="HasPlaque"/> is <see langword="false"/>.</returns>
    public WorldBounds PlaqueBox(float innerHeight)
    {
        var center = PlaqueCenter(innerHeight);
        var half = new Vector3(PlaqueWidth * 0.5f, PlaqueHeight * 0.5f, 0);
        return new WorldBounds(
            new Vector3(center.X - half.X, center.Y - half.Y, -Depth),
            new Vector3(center.X + half.X, center.Y + half.Y, center.Z));
    }

    /// <summary>
    /// Gives the full height of the frame and the plaque under it.
    /// </summary>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The canvas, two rails and, with a plaque, the gap and the plaque height, in meters.</returns>
    public float PaintingHeight(float innerHeight) =>
        innerHeight + (2 * Rail) + (HasPlaque ? PlaqueGap + PlaqueHeight : 0);

    /// <summary>
    /// Gets the vertical distance in meters from the canvas center to the center of <see cref="PaintingHeight"/>. It is negative with a plaque and 0 without one.
    /// </summary>
    public float PaintingCenterOffset => HasPlaque ? -(PlaqueGap + PlaqueHeight) * 0.5f : 0;

    /// <summary>
    /// Names the part of the frame at a point. A point past the canvas on both axes is a corner. A point on the inner <see cref="EdgeShare"/> of a rail is an edge.
    /// </summary>
    /// <param name="local">The point in the window's local space.</param>
    /// <param name="innerWidth">The canvas width in meters.</param>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <returns>The part. Any other point on the frame is <see cref="WorldFramePart.Face"/>.</returns>
    public WorldFramePart PartAt(Vector3 local, float innerWidth, float innerHeight)
    {
        var outX = MathF.Abs(local.X) - (innerWidth * 0.5f);
        var outY = MathF.Abs(local.Y) - (innerHeight * 0.5f);
        var right = local.X >= 0;
        var top = local.Y >= 0;
        if (outX > 0 && outY > 0)
        {
            return (right, top) switch
            {
                (true, true) => WorldFramePart.TopRight,
                (false, true) => WorldFramePart.TopLeft,
                (true, false) => WorldFramePart.BottomRight,
                _ => WorldFramePart.BottomLeft,
            };
        }

        var edge = Rail * EdgeShare;
        if (outX >= outY)
        {
            return outX < edge ? (right ? WorldFramePart.Right : WorldFramePart.Left) : WorldFramePart.Face;
        }

        return outY < edge ? (top ? WorldFramePart.Top : WorldFramePart.Bottom) : WorldFramePart.Face;
    }
}
