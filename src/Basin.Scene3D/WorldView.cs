using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// The camera matrices for one output's slice of the layout, or for the view through a portal.
/// </summary>
public readonly struct WorldView
{
    private WorldView(
        in Matrix4x4 cameraToWorld, in Matrix4x4 worldToCamera, in Matrix4x4 projection,
        in Box union, in Box slice, float fieldOfView, float near, float far)
    {
        CameraToWorld = cameraToWorld;
        WorldToCamera = worldToCamera;
        Projection = projection;
        ViewProjection = worldToCamera * projection;
        Union = union;
        Slice = slice;
        FieldOfView = fieldOfView;
        Near = near;
        Far = far;
    }

    /// <summary>
    /// Gets the camera pose in world space.
    /// </summary>
    public Matrix4x4 CameraToWorld { get; }

    /// <summary>
    /// Gets the inverse of <see cref="CameraToWorld"/>.
    /// </summary>
    public Matrix4x4 WorldToCamera { get; }

    /// <summary>
    /// Gets the off-axis projection, with clip depth in [0, 1] and clip y up. A portal view has an oblique near plane.
    /// </summary>
    public Matrix4x4 Projection { get; }

    /// <summary>
    /// Gets <see cref="WorldToCamera"/> times <see cref="Projection"/>.
    /// </summary>
    public Matrix4x4 ViewProjection { get; }

    /// <summary>
    /// Gets the union of the output layout, in layout pixels.
    /// </summary>
    public Box Union { get; }

    /// <summary>
    /// Gets the output's box in the layout, in layout pixels.
    /// </summary>
    public Box Slice { get; }

    /// <summary>
    /// Gets the vertical field of view of the union, in radians.
    /// </summary>
    public float FieldOfView { get; }

    /// <summary>
    /// Gets the distance to the near plane, in meters.
    /// </summary>
    public float Near { get; }

    /// <summary>
    /// Gets the distance to the far plane, in meters.
    /// </summary>
    public float Far { get; }

    /// <summary>
    /// Gets the eye position in world space.
    /// </summary>
    public Vector3 Eye => CameraToWorld.Translation;

    /// <summary>
    /// Builds the view of one output's slice from a camera pose.
    /// </summary>
    /// <param name="cameraToWorld">The camera pose. It must be invertible.</param>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="slice">The output's box in the layout.</param>
    /// <param name="fieldOfView">The vertical field of view of the union, in radians.</param>
    /// <param name="near">The distance to the near plane, in meters.</param>
    /// <param name="far">The distance to the far plane, in meters.</param>
    /// <returns>The view of the slice.</returns>
    /// <exception cref="ArgumentException">The pose is not invertible, or <paramref name="union"/> has no area.</exception>
    public static WorldView Create(
        in Matrix4x4 cameraToWorld, in Box union, in Box slice, float fieldOfView, float near, float far)
    {
        if (!Matrix4x4.Invert(cameraToWorld, out var worldToCamera))
        {
            throw new ArgumentException("The camera pose is not invertible.", nameof(cameraToWorld));
        }

        var projection = SpannedFrustum.Projection(union, slice, fieldOfView, near, far);
        return new WorldView(cameraToWorld, worldToCamera, projection, union, slice, fieldOfView, near, far);
    }

    /// <summary>
    /// Builds the view through a portal. The near plane becomes <paramref name="clipPlane"/>, so geometry behind the target portal is clipped.
    /// </summary>
    /// <param name="transfer">The portal transfer from <see cref="WorldPortal.TransferTo"/>.</param>
    /// <param name="clipPlane">The target portal's plane in world space, from <see cref="WorldPortal.Plane"/>.</param>
    /// <returns>The view on the far side of the portal, with the same slice.</returns>
    public WorldView Through(in Matrix4x4 transfer, in Vector4 clipPlane)
    {
        var cameraToWorld = CameraToWorld * transfer;
        Matrix4x4.Invert(cameraToWorld, out var worldToCamera);
        var cameraPlane = Vector4.Transform(clipPlane, Matrix4x4.Transpose(cameraToWorld));
        var projection = Oblique(Projection, cameraPlane);
        return new WorldView(cameraToWorld, worldToCamera, projection, Union, Slice, FieldOfView, Near, Far);
    }

    /// <summary>
    /// Builds the ray from the eye through a layout pixel. Each output's view of one camera gives the same ray for the same pixel.
    /// </summary>
    /// <param name="layoutX">The x coordinate in layout pixels.</param>
    /// <param name="layoutY">The y coordinate in layout pixels.</param>
    /// <returns>A world-space ray with a unit direction.</returns>
    public WorldRay RayAt(double layoutX, double layoutY)
    {
        var direction = SpannedFrustum.CameraDirection(Union, layoutX, layoutY, FieldOfView);
        return new WorldRay(Eye, Vector3.Normalize(Vector3.TransformNormal(direction, CameraToWorld)));
    }

    /// <summary>
    /// Projects a world point to a layout pixel through this view's slice.
    /// </summary>
    /// <param name="world">The point in world space.</param>
    /// <param name="layoutX">The x coordinate in layout pixels.</param>
    /// <param name="layoutY">The y coordinate in layout pixels.</param>
    /// <param name="depth">The clip depth, 0 at the near plane and 1 at the far plane.</param>
    /// <returns><see langword="false"/> when the point is at or behind the eye. The outputs are then 0.</returns>
    public bool TryProject(Vector3 world, out double layoutX, out double layoutY, out float depth)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), ViewProjection);
        if (clip.W <= 1e-6f)
        {
            layoutX = layoutY = 0;
            depth = 0;
            return false;
        }

        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        depth = clip.Z / clip.W;
        layoutX = Slice.X + ((x + 1) * 0.5 * Slice.Width);
        layoutY = Slice.Y + ((1 - y) * 0.5 * Slice.Height);
        return true;
    }

    /// <summary>
    /// Builds the culling frustum of the view.
    /// </summary>
    /// <returns>The six planes of <see cref="ViewProjection"/>.</returns>
    public WorldFrustum Frustum() => WorldFrustum.From(ViewProjection);

    private static Matrix4x4 Oblique(in Matrix4x4 projection, Vector4 plane)
    {
        if (plane.W > 0)
        {
            plane = -plane;
        }

        var q = new Vector4(
            (MathF.Sign(plane.X) + projection.M31) / projection.M11,
            (MathF.Sign(plane.Y) + projection.M32) / projection.M22,
            -1,
            (1 + projection.M33) / projection.M43);
        var scale = 1f / Vector4.Dot(plane, q);
        var c = plane * scale;
        var result = projection;
        result.M13 = c.X;
        result.M23 = c.Y;
        result.M33 = c.Z;
        result.M43 = c.W;
        return result;
    }
}
