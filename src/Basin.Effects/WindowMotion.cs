namespace Basin.Effects;

/// <summary>
/// Integrates KWin's window motion on one axis. Each 5 ms Euler step blends the velocity toward the distance times a strength, so the value can overshoot a little.
/// </summary>
public struct WindowMotion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowMotion"/> struct at rest on a value.
    /// </summary>
    /// <param name="initial">The start value, which is also the target.</param>
    /// <param name="strength">The fraction of the distance that each step adds to the velocity.</param>
    /// <param name="smoothness">The weight of the old velocity against the new pull.</param>
    public WindowMotion(double initial, double strength = 0.08, double smoothness = 4.0)
    {
        Value = initial;
        Start = initial;
        Target = initial;
        Velocity = 0;
        Strength = strength;
        Smoothness = smoothness;
    }

    /// <summary>
    /// Gets or sets the current value.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Gets the value at the last <see cref="SetTarget"/>.
    /// </summary>
    public double Start { get; private set; }

    /// <summary>
    /// Gets the value that the motion moves toward.
    /// </summary>
    public double Target { get; private set; }

    /// <summary>
    /// Gets or sets the change of the value per 5 ms step.
    /// </summary>
    public double Velocity { get; set; }

    /// <summary>
    /// Gets or sets the fraction of the distance that each step adds to the velocity.
    /// </summary>
    public double Strength { get; set; }

    /// <summary>
    /// Gets or sets the weight of the old velocity against the new pull. A larger value gives a slower, smoother motion.
    /// </summary>
    public double Smoothness { get; set; }

    /// <summary>
    /// Gets the signed distance from <see cref="Value"/> to <see cref="Target"/>.
    /// </summary>
    public readonly double Distance => Target - Value;

    /// <summary>
    /// Sets a new target and records the current value as <see cref="Start"/>. The velocity does not change.
    /// </summary>
    /// <param name="target">The new target.</param>
    public void SetTarget(double target)
    {
        Start = Value;
        Target = target;
    }

    /// <summary>
    /// Advances the motion by a time, in whole 5 ms steps and at least one step. A motion at rest on its target does not change.
    /// </summary>
    /// <param name="millis">The elapsed time, in milliseconds.</param>
    public void Calculate(double millis)
    {
        if (Value == Target && Velocity == 0)
        {
            return;
        }

        var steps = Math.Max(1, (int)(millis / 5));
        for (var i = 0; i < steps; i++)
        {
            var strength = (Target - Value) * Strength;
            Velocity = ((Smoothness * Velocity) + strength) / (Smoothness + 1.0);
            Value += Velocity;
        }
    }

    /// <summary>
    /// Moves the value to the target and stops the motion.
    /// </summary>
    public void Finish()
    {
        Value = Target;
        Velocity = 0;
    }

    /// <summary>
    /// Tests KWin's stop rule, which looks at the remaining distance and the speed in the direction of travel.
    /// </summary>
    /// <returns><see langword="true"/> when the distance is zero, or the distance is below 0.5 and the velocity below 0.2 in that direction.</returns>
    public readonly bool IsSettled()
    {
        if (Distance == 0)
        {
            return true;
        }

        var sign = Target <= Start ? -1 : 1;
        return Distance * sign / 0.5 < 1.0 && Velocity * sign / 0.2 < 1.0;
    }
}
