using Basin.Diagnostics;

namespace Basin.SoftKeyboard;

public sealed record SoftKeyboardSessionOptions
{
    public const string XkbStandard = "xkb-standard";
    public const string XkbFull = "xkb-full";

    public IReadOnlyList<string> Layouts { get; init; } = [];

    public string? Locale { get; init; }

    public string? UserLayoutDirectory { get; init; }

    public SoftKeyboardPolicy Policy { get; init; } = SoftKeyboardPolicy.OnTouchFocus;

    public string? StatePath { get; init; }

    public KeyTiming Timing { get; init; } = KeyTiming.Default;

    public bool Diamonds { get; init; } = true;

    public bool SpacesToPeriod { get; init; } = true;

    public KeyboardSize Size { get; init; } = KeyboardSize.Medium;

    public int TypistTouchId { get; init; } = 900;

    public BasinLogger? Log { get; init; }
}
