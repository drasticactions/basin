namespace Basin.Capabilities;

/// <summary>
/// The keyboard aids for people who cannot press keys together, press keys briefly or press keys once. Every aid is off by default.
/// </summary>
public sealed record KeyboardAidOptions
{
    /// <summary>
    /// Gets a value indicating whether sticky keys are on. A modifier tapped alone stays down for the next key, and a second tap locks it.
    /// </summary>
    public bool Sticky { get; init; }

    /// <summary>
    /// Gets a value indicating whether five taps of Shift turn sticky keys on and off.
    /// </summary>
    public bool StickyShortcut { get; init; }

    /// <summary>
    /// Gets the time in milliseconds that a key must stay down before it counts. Zero turns slow keys off.
    /// </summary>
    public int SlowMs { get; init; }

    /// <summary>
    /// Gets the time in milliseconds after a release in which a new press of the same key is ignored. Zero turns bounce keys off.
    /// </summary>
    public int BounceMs { get; init; }

    /// <summary>
    /// Gets a value indicating whether the keypad moves and clicks the pointer.
    /// </summary>
    public bool MouseKeys { get; init; }

    /// <summary>
    /// Gets the highest pointer speed of mouse keys, in logical pixels per second. The default is 1000.
    /// </summary>
    public double MouseKeysMaxSpeed { get; init; } = 1000;

    /// <summary>
    /// Gets the time in milliseconds that mouse keys take to reach the highest speed. The default is 1000.
    /// </summary>
    public int MouseKeysAccelerationMs { get; init; } = 1000;
}
