namespace Basin.Effects;

public readonly record struct AnimationDuration(double BaseMillis, double Factor = 1.0)
{
    public static AnimationDuration Zero => new(0);

    public double Millis => Math.Max(0, BaseMillis) * Math.Max(0, Factor);

    public long Nanos => (long)Math.Round(Millis * 1_000_000.0);

    public bool IsDisabled => Millis <= 0;

    public AnimationDuration WithFactor(double factor) => this with { Factor = factor };

    public AnimationDuration WithBase(double baseMillis) => this with { BaseMillis = baseMillis };
}
