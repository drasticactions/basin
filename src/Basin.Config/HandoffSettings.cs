namespace Basin.Config;

/// <summary>
/// The values of the <c>[handoff]</c> table that control how a compositor takes over the frame of the previous one.
/// </summary>
public sealed record HandoffSettings
{
    /// <summary>
    /// Gets the mode that decides when the last frame is kept for the next compositor. The default is <see cref="HandoffMode.Auto"/>.
    /// </summary>
    public HandoffMode Mode { get; init; }

    /// <summary>
    /// Gets the transition from the inherited frame to the first new frame. The default is <see cref="HandoffTransitionKind.Crossfade"/>.
    /// </summary>
    public HandoffTransitionKind Transition { get; init; }

    /// <summary>
    /// Gets the length of the crossfade in milliseconds. <see cref="Read"/> clamps it to 0 to 5000. The default is 400.
    /// </summary>
    public int DurationMs { get; init; } = 400;

    /// <summary>
    /// Gets the longest time in milliseconds that the inherited frame is held before the transition starts anyway.
    /// <see cref="Read"/> clamps it to 0 to 30000. The default is 3000.
    /// </summary>
    public int HoldLimitMs { get; init; } = 3000;

    /// <summary>
    /// Reads the <c>mode</c>, <c>transition</c>, <c>duration_ms</c> and <c>hold_limit_ms</c> keys. A missing or invalid key keeps its default.
    /// </summary>
    /// <param name="section">The <c>[handoff]</c> table, or <see langword="null"/> when the file has none.</param>
    /// <returns>The settings, with defaults for each key that is not set.</returns>
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
