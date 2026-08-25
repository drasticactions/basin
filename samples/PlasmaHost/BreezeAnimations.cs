namespace PlasmaHost;

internal static class BreezeAnimations
{
    private static TimeSpan? _duration;

    public static TimeSpan Duration => _duration ??= Load();

    private static TimeSpan Load()
    {
        var path = KdeIni.ConfigPath("breezerc");
        if (KdeIni.ReadEntry(path, "Common", "AnimationsEnabled") is { } enabled &&
            !enabled.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.Zero;
        }

        var millis = KdeIni.ReadEntry(path, "Common", "AnimationsDuration");
        return millis is not null && int.TryParse(millis, out var value) && value >= 0
            ? TimeSpan.FromMilliseconds(value)
            : TimeSpan.FromMilliseconds(300);
    }
}
