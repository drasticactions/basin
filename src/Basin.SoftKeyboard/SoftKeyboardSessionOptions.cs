using Basin.Diagnostics;

namespace Basin.SoftKeyboard;

/// <summary>
/// The configuration of a <see cref="SoftKeyboardSession"/>. <see cref="SoftKeyboardSession.Reload"/> applies a new value at run time.
/// </summary>
public sealed record SoftKeyboardSessionOptions
{
    /// <summary>
    /// The layout id of the Windows 8 standard shape built from the seat's xkb keymap.
    /// </summary>
    public const string XkbStandard = "xkb-standard";
    /// <summary>
    /// The layout id of the Windows 8 full shape built from the seat's xkb keymap.
    /// </summary>
    public const string XkbFull = "xkb-full";

    /// <summary>
    /// Gets the layout ids that the language key cycles through, in order. An empty list selects the default layout for <see cref="Locale"/>.
    /// </summary>
    public IReadOnlyList<string> Layouts { get; init; } = [];

    /// <summary>
    /// Gets the locale that picks the default layout when <see cref="Layouts"/> is empty, for example <c>fr_FR</c>.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Gets the directory whose <c>ID.toml</c> files shadow the built-in layouts, or <see langword="null"/> for the built-in layouts only.
    /// </summary>
    public string? UserLayoutDirectory { get; init; }

    /// <summary>
    /// Gets the rule that decides when a focused field raises the keyboard.
    /// </summary>
    public SoftKeyboardPolicy Policy { get; init; } = SoftKeyboardPolicy.OnTouchFocus;

    /// <summary>
    /// Gets the path of the JSON file that keeps the chosen size and layout, or <see langword="null"/> to keep no state.
    /// </summary>
    public string? StatePath { get; init; }

    /// <summary>
    /// Gets the timing constants that the engine and the router use.
    /// </summary>
    public KeyTiming Timing { get; init; } = KeyTiming.Default;

    /// <summary>
    /// Gets a value indicating whether the hit test applies the row-edge correction between rows.
    /// </summary>
    public bool Diamonds { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a fast second Space types a period and a space.
    /// </summary>
    public bool SpacesToPeriod { get; init; } = true;

    /// <summary>
    /// Gets the size to use when the state file holds no size.
    /// </summary>
    public KeyboardSize Size { get; init; } = KeyboardSize.Medium;

    /// <summary>
    /// Gets the touch id that <see cref="SoftKeyboardTyper"/> uses for its taps. It must not collide with a real touch id.
    /// </summary>
    public int TypistTouchId { get; init; } = 900;

    /// <summary>
    /// Gets the logger for the session, or <see langword="null"/> for the library's <c>keyboard</c> logger.
    /// </summary>
    public BasinLogger? Log { get; init; }
}
