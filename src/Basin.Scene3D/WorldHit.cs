using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// The nearest thing that a ray hits in a <see cref="World"/>.
/// </summary>
/// <param name="Kind">What the ray hit. <see cref="WorldHitKind.None"/> means it hit nothing.</param>
/// <param name="Window">The window that the ray hit, or <see langword="null"/> for room geometry and a miss.</param>
/// <param name="Layer">The index of the hit layer in <see cref="WorldWindow.Layers"/>, or -1 when no layer was hit.</param>
/// <param name="Surface">The surface that takes the input, or <see langword="null"/> when the hit gives no surface.</param>
/// <param name="SurfaceX">The x coordinate in the surface's logical pixels. For a frame it is relative to the canvas, and for a plaque it is in plaque pixels.</param>
/// <param name="SurfaceY">The y coordinate, in the same space as <paramref name="SurfaceX"/>.</param>
/// <param name="Point">The hit point in world space.</param>
/// <param name="Normal">The world-space normal of the face that the ray hit.</param>
/// <param name="Distance">The ray parameter of the hit. It is <see cref="float.MaxValue"/> for a miss.</param>
/// <param name="Part">The part of a frame that the ray hit, when <paramref name="Kind"/> is <see cref="WorldHitKind.Frame"/>.</param>
public readonly record struct WorldHit(
    WorldHitKind Kind,
    WorldWindow? Window,
    int Layer,
    Surface? Surface,
    double SurfaceX,
    double SurfaceY,
    Vector3 Point,
    Vector3 Normal,
    float Distance,
    WorldFramePart Part = WorldFramePart.None)
{
    /// <summary>
    /// Gets a hit that hit nothing, at the largest distance, so any real hit is nearer.
    /// </summary>
    public static WorldHit Miss => new(WorldHitKind.None, null, -1, null, 0, 0, Vector3.Zero, Vector3.Zero, float.MaxValue);
}
