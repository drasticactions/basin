namespace Basin.SoftKeyboard;

/// <summary>
/// The timing constants of the soft keyboard. All times are in milliseconds and all distances are in logical pixels.
/// </summary>
public sealed record KeyTiming
{
    /// <summary>
    /// The LunaCE values, which every property has by default.
    /// </summary>
    public static readonly KeyTiming Default = new();

    /// <summary>
    /// Gets the time within which a second Shift tap locks Shift, and a second modifier tap locks the modifier.
    /// </summary>
    public int ShiftDoubleTapMs { get; init; } = 500;

    /// <summary>
    /// Gets the hold time that opens a key's long-press list.
    /// </summary>
    public int LongPressMs { get; init; } = 350;

    /// <summary>
    /// Gets the hold time that opens a long-press list for Hide, or for any key while <see cref="SoftKeyboardEngine.SlowLongPress"/> is on.
    /// </summary>
    public int SlowLongPressMs { get; init; } = 750;

    /// <summary>
    /// Gets the hold time before the first repeat of a repeating key.
    /// </summary>
    public int RepeatDelayMs { get; init; } = 350;

    /// <summary>
    /// Gets the time between two repeats of a repeating key.
    /// </summary>
    public int RepeatIntervalMs { get; init; } = 120;

    /// <summary>
    /// Gets the time between two word deletes of a held Backspace.
    /// </summary>
    public int WordDeleteIntervalMs { get; init; } = 275;

    /// <summary>
    /// Gets the hold time of Backspace after which it deletes words and not characters.
    /// </summary>
    public int WordDeleteAfterMs { get; init; } = 1850;

    /// <summary>
    /// Gets the time within which a second Space types a period.
    /// </summary>
    public int DoubleSpaceMs { get; init; } = 1000;

    /// <summary>
    /// Gets the height of the band above the keyboard that still accepts a touch, in logical pixels.
    /// </summary>
    public float GraceBand { get; init; } = 40;

    /// <summary>
    /// Gets the time after a touch begins during which the grace band accepts touches.
    /// </summary>
    public int GraceBandMs { get; init; } = 500;

    /// <summary>
    /// Gets the travel of one touch, in logical pixels, after which it becomes a trace.
    /// </summary>
    public float TraceThreshold { get; init; } = 35;
}
