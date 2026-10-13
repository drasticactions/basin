namespace Basin.Config;

public sealed record HandoffSettings
{
    public HandoffMode Mode { get; init; }

    public HandoffTransitionKind Transition { get; init; }

    public int DurationMs { get; init; } = 400;

    public int HoldLimitMs { get; init; } = 3000;

    public static HandoffSettings Read(TomlReader? section)
    {
        var settings = new HandoffSettings();
        if (section is null)
        {
            return settings;
        }

        return settings with
        {
            Mode = HandoffChoice.Parse(section.Choice("mode", "auto", "auto", "retain", "off")) ?? HandoffMode.Auto,
            Transition = section.Choice("transition", "crossfade", "crossfade", "cut") == "cut"
                ? HandoffTransitionKind.Cut
                : HandoffTransitionKind.Crossfade,
            DurationMs = Math.Clamp(section.Number("duration_ms", settings.DurationMs), 0, 5000),
            HoldLimitMs = Math.Clamp(section.Number("hold_limit_ms", settings.HoldLimitMs), 0, 30000),
        };
    }
}
