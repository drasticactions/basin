using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// One camera that spans every output: an eye position, a yaw, a pitch and no roll.
/// </summary>
public sealed class WorldCamera
{
    private const float PitchLimit = 1.55f;
    private float _pitch;

    /// <summary>
    /// Gets or sets the eye position in world space, in meters.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the turn round the world y axis, in radians. At 0 the camera looks down world -z.
    /// </summary>
    public float Yaw { get; set; }

    /// <summary>
    /// Gets or sets the tilt up or down, in radians. The setter clamps it to 1.55 each way, short of straight up and straight down.
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -PitchLimit, PitchLimit);
    }

    /// <summary>
    /// Gets or sets the vertical field of view of the layout union, in radians. The default is 70 degrees.
    /// </summary>
    public float FieldOfView { get; set; } = MathF.PI * 70f / 180f;

    /// <summary>
    /// Gets or sets the distance to the near plane, in meters.
    /// </summary>
    public float Near { get; set; } = 0.05f;

    /// <summary>
    /// Gets or sets the distance to the far plane, in meters.
    /// </summary>
    public float Far { get; set; } = 200f;

    /// <summary>
    /// Gets the rotation from camera space to world space.
    /// </summary>
    public Quaternion Orientation => Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, 0);

    /// <summary>
    /// Gets the unit view direction in world space, with the pitch.
    /// </summary>
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);

    /// <summary>
    /// Gets the unit right vector in world space, with the pitch.
    /// </summary>
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);

    /// <summary>
    /// Gets the unit up vector of the camera in world space.
    /// </summary>
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);

    /// <summary>
    /// Gets the view direction on the ground plane. It ignores the pitch, so walking along it keeps the height.
    /// </summary>
    public Vector3 Flat => new(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));

    /// <summary>
    /// Gets the right vector on the ground plane. It ignores the pitch.
    /// </summary>
    public Vector3 FlatRight => new(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));

    /// <summary>
    /// Gets the camera pose: the orientation, then the translation to <see cref="Position"/>.
    /// </summary>
    public Matrix4x4 CameraToWorld =>
        Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Builds the view of one output's slice of the layout.
    /// </summary>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="slice">The output's box in the layout.</param>
    /// <returns>The view with an off-axis projection over <paramref name="slice"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="union"/> has no area.</exception>
    public WorldView ViewOf(in Box union, in Box slice) =>
        WorldView.Create(CameraToWorld, union, slice, FieldOfView, Near, Far);

    /// <summary>
    /// Builds the ray through a layout point from the output under it, or from the first output when no output is under it.
    /// </summary>
    /// <param name="layout">The output layout that the camera spans.</param>
    /// <param name="x">The x coordinate in layout pixels.</param>
    /// <param name="y">The y coordinate in layout pixels.</param>
    /// <param name="ray">The world-space ray with a unit direction, or the default when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the layout has no output or an empty box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public bool TryRayAt(OutputLayout layout, double x, double y, out WorldRay ray)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ray = default;
        var output = layout.OutputAt(x, y);
        if (output is null)
        {
            var outputs = layout.Outputs;
            if (outputs.Length == 0)
            {
                return false;
            }

            output = outputs[0].Output;
        }

        var union = layout.Bounds;
        var slice = layout.BoxOf(output);
        if (union.IsEmpty || slice.IsEmpty)
        {
            return false;
        }

        ray = ViewOf(union, slice).RayAt(x, y);
        return true;
    }
}
