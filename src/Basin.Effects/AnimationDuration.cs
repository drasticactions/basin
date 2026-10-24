namespace Basin.Effects;

/// <summary>
/// A base duration and the desktop animation factor that scales it. A product of zero turns the animation off.
/// </summary>
/// <param name="BaseMillis">The duration at a factor of 1, in milliseconds.</param>
/// <param name="Factor">The animation speed factor that the desktop sets. A larger factor gives a slower animation.</param>
public readonly record struct AnimationDuration(double BaseMillis, double Factor = 1.0)
{
    /// <summary>
    /// Gets a duration that turns the animation off.
    /// </summary>
    public static AnimationDuration Zero => new(0);

    /// <summary>
    /// Gets the scaled duration in milliseconds. Negative inputs count as zero.
    /// </summary>
    public double Millis => Math.Max(0, BaseMillis) * Math.Max(0, Factor);

    /// <summary>
    /// Gets the scaled duration in nanoseconds, rounded to the nearest nanosecond.
    /// </summary>
    public long Nanos => (long)Math.Round(Millis * 1_000_000.0);

    /// <summary>
    /// Gets a value indicating whether the scaled duration is zero. An effect does not run at all with a disabled duration.
    /// </summary>
    public bool IsDisabled => Millis <= 0;

    /// <summary>
    /// Returns a copy with a different animation factor.
    /// </summary>
    /// <param name="factor">The new factor.</param>
    /// <returns>The copy with the base duration unchanged.</returns>
    public AnimationDuration WithFactor(double factor) => this with { Factor = factor };

    /// <summary>
    /// Returns a copy with a different base duration.
    /// </summary>
    /// <param name="baseMillis">The new base duration in milliseconds.</param>
    /// <returns>The copy with the factor unchanged.</returns>
    public AnimationDuration WithBase(double baseMillis) => this with { BaseMillis = baseMillis };
}
