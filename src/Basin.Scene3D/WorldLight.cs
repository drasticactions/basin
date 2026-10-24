using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A point light with linear falloff and an optional flicker.
/// </summary>
public struct WorldLight
{
    /// <summary>
    /// The position in world space.
    /// </summary>
    public Vector3 Position;
    /// <summary>
    /// The linear RGB color.
    /// </summary>
    public Vector3 Color;
    /// <summary>
    /// The distance in meters at which the light falls to zero.
    /// </summary>
    public float Range;
    /// <summary>
    /// The base intensity.
    /// </summary>
    public float Intensity;
    /// <summary>
    /// The flicker amplitude as a fraction of <see cref="Intensity"/>. Zero gives a steady light.
    /// </summary>
    public float Flicker;
    /// <summary>
    /// The flicker phase in radians, so lights with one amplitude do not flicker together.
    /// </summary>
    public float Phase;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldLight"/> struct.
    /// </summary>
    /// <param name="position">The position in world space.</param>
    /// <param name="color">The linear RGB color.</param>
    /// <param name="range">The distance in meters at which the light falls to zero.</param>
    /// <param name="intensity">The base intensity.</param>
    /// <param name="flicker">The flicker amplitude as a fraction of the intensity.</param>
    /// <param name="phase">The flicker phase in radians.</param>
    public WorldLight(Vector3 position, Vector3 color, float range, float intensity, float flicker = 0, float phase = 0)
    {
        Position = position;
        Color = color;
        Range = range;
        Intensity = intensity;
        Flicker = flicker;
        Phase = phase;
    }

    /// <summary>
    /// Gives the intensity at a time. The result is a pure function of the time, so a fixed frame time gives a stable image.
    /// </summary>
    /// <param name="nanos">The time in nanoseconds, for example the target present time of the frame.</param>
    /// <returns><see cref="Intensity"/> when <see cref="Flicker"/> is zero or less, or the flickered intensity.</returns>
    public readonly float IntensityAt(long nanos)
    {
        if (Flicker <= 0)
        {
            return Intensity;
        }

        var seconds = (nanos % 1_000_000_000_000L) / 1e9f;
        var wave = (0.6f * MathF.Sin((seconds * 7.3f) + Phase)) + (0.4f * MathF.Sin((seconds * 13.1f) + (Phase * 1.7f)));
        return Intensity * (1f + (Flicker * wave));
    }
}
