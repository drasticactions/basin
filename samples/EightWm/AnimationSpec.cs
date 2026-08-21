namespace EightWm;

internal readonly record struct AnimationSpec(
    Animation Name,
    MotionAxis Axis,
    Track Offset,
    Track Scale,
    Track Opacity,
    uint StaggerMs,
    uint StaggerCapMs)
{
    public uint DurationMs => Math.Max(Offset.EndMillis, Math.Max(Scale.EndMillis, Opacity.EndMillis));

    public uint DelayFor(int index)
    {
        if (StaggerMs == 0 || index <= 0)
        {
            return 0;
        }

        var delay = (uint)Math.Min((long)StaggerMs * index, uint.MaxValue);
        return StaggerCapMs > 0 && delay > StaggerCapMs ? StaggerCapMs : delay;
    }
}
