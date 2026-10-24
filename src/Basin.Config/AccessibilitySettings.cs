namespace Basin.Config;

/// <summary>
/// The values of the <c>[accessibility]</c> table that every compositor reads.
/// </summary>
public sealed record AccessibilitySettings
{
    /// <summary>
    /// Gets the mode of the <c>enabled</c> key. The default is <see cref="AccessibilityMode.Auto"/>.
    /// </summary>
    public AccessibilityMode Enabled { get; init; }

    /// <summary>
    /// Gets the screen reader mode of the <c>screen-reader</c> key: <c>off</c>, <c>toggle</c> or <c>on</c>. With <c>toggle</c> a chord starts and stops the reader.
    /// With <c>on</c> the reader starts with the session. The default is <c>toggle</c>.
    /// </summary>
    public string ScreenReader { get; init; } = "toggle";

    /// <summary>
    /// Gets the command of the <c>screen-reader-command</c> key. The default is <c>orca --replace</c>.
    /// </summary>
    public IReadOnlyList<string> ScreenReaderCommand { get; init; } = ["orca", "--replace"];

    /// <summary>
    /// Gets a value indicating whether the <c>high-contrast</c> key asks for high-contrast chrome and tells applications so. The default is <see langword="false"/>.
    /// </summary>
    public bool HighContrast { get; init; }

    /// <summary>
    /// Gets a value indicating whether the <c>reduced-motion</c> key turns animations off and tells applications so. The default is <see langword="false"/>.
    /// </summary>
    public bool ReducedMotion { get; init; }

    /// <summary>
    /// Gets the keyboard aids of the <c>[accessibility.keyboard]</c> table: <c>sticky</c>, <c>sticky-shortcut</c>, <c>slow-ms</c>, <c>bounce-ms</c>, <c>mouse-keys</c> and <c>mouse-keys-speed</c>. Every aid is off by default.
    /// </summary>
    public Basin.Capabilities.KeyboardAidOptions Keyboard { get; init; } = new();

    /// <summary>
    /// Reads the <c>enabled</c>, <c>screen-reader</c>, <c>screen-reader-command</c>, <c>high-contrast</c> and <c>reduced-motion</c> keys and the <c>keyboard</c> table. A missing or invalid key keeps its default. Other keys stay for the caller.
    /// </summary>
    /// <param name="section">The <c>[accessibility]</c> table, or <see langword="null"/> when the file has none.</param>
    /// <returns>The settings, with defaults for each key that is not set.</returns>
    public static AccessibilitySettings Read(TomlReader? section)
    {
        var settings = new AccessibilitySettings();
        if (section is null)
        {
            return settings;
        }

        return settings with
        {
            Enabled = ParseEnabled(section.Choice("enabled", "auto", "auto", "on", "off")) ?? AccessibilityMode.Auto,
            ScreenReader = section.Choice("screen-reader", settings.ScreenReader, "off", "toggle", "on"),
            ScreenReaderCommand = section.Words("screen-reader-command") is { Length: > 0 } command ? command : settings.ScreenReaderCommand,
            HighContrast = section.Flag("high-contrast", settings.HighContrast),
            ReducedMotion = section.Flag("reduced-motion", settings.ReducedMotion),
            Keyboard = ReadKeyboard(section.Section("keyboard")),
        };
    }

    private static Basin.Capabilities.KeyboardAidOptions ReadKeyboard(TomlReader? section)
    {
        var aids = new Basin.Capabilities.KeyboardAidOptions();
        if (section is null)
        {
            return aids;
        }

        return aids with
        {
            Sticky = section.Flag("sticky", aids.Sticky),
            StickyShortcut = section.Flag("sticky-shortcut", aids.StickyShortcut),
            SlowMs = Math.Max(0, section.Number("slow-ms", aids.SlowMs)),
            BounceMs = Math.Max(0, section.Number("bounce-ms", aids.BounceMs)),
            MouseKeys = section.Flag("mouse-keys", aids.MouseKeys),
            MouseKeysMaxSpeed = Math.Max(1.0, section.Number("mouse-keys-speed", aids.MouseKeysMaxSpeed)),
        };
    }

    /// <summary>
    /// Parses a value of the <c>enabled</c> key, for a compositor whose configuration is not TOML.
    /// </summary>
    /// <param name="value">The value: <c>auto</c>, <c>on</c> or <c>off</c>.</param>
    /// <returns>The mode, or <see langword="null"/> for any other value.</returns>
    public static AccessibilityMode? ParseEnabled(string value) => value switch
    {
        "auto" => AccessibilityMode.Auto,
        "on" => AccessibilityMode.On,
        "off" => AccessibilityMode.Off,
        _ => null,
    };
}
