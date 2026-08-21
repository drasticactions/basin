namespace EightWm;

internal readonly record struct Track(
    double From,
    double To,
    uint DurationMs,
    uint DelayMs,
    AnimationCurve Curve)
{
    public static Track None => default;

    public bool IsEmpty => DurationMs == 0;

    public uint EndMillis => DelayMs + DurationMs;
}
