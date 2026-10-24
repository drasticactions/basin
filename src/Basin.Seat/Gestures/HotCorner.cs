namespace Basin.Seat;

/// <summary>
/// Fires once when the pointer rests in one corner of an output for a delay. The caller polls it with the time.
/// </summary>
public sealed class HotCorner
{
    private bool _inside;
    private bool _fired;
    private uint _enteredMs;

    /// <summary>
    /// Gets or sets the corner to watch. <see cref="ScreenCorner.None"/> turns the hot corner off.
    /// </summary>
    public ScreenCorner Corner { get; set; } = ScreenCorner.TopLeft;

    /// <summary>
    /// Gets or sets the size of the corner square in layout units.
    /// </summary>
    public double Size { get; set; } = 6;

    /// <summary>
    /// Gets or sets the time in milliseconds that the pointer must stay in the corner before it fires.
    /// </summary>
    public uint DelayMs { get; set; } = 150;

    /// <summary>
    /// Gets a value indicating whether the pointer is in the corner and the corner has not fired yet.
    /// </summary>
    public bool IsArmed => _inside && !_fired;

    /// <summary>
    /// Gets the time in milliseconds at which an armed corner fires. Schedule a <see cref="Poll"/> at this time.
    /// </summary>
    public uint DueMs => _enteredMs + DelayMs;

    /// <summary>
    /// Finds the corner of a box that a point is near. The point is not checked against the box itself.
    /// </summary>
    /// <param name="box">The output box in layout coordinates.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <param name="size">The size of each corner square.</param>
    /// <returns>The corner, or <see cref="ScreenCorner.None"/>.</returns>
    public static ScreenCorner At(in Box box, double x, double y, double size)
    {
        var left = x - box.X <= size;
        var right = x >= box.Right - size;
        var top = y - box.Y <= size;
        var bottom = y >= box.Bottom - size;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => ScreenCorner.TopLeft,
            (_, true, true, _) => ScreenCorner.TopRight,
            (true, _, _, true) => ScreenCorner.BottomLeft,
            (_, true, _, true) => ScreenCorner.BottomRight,
            _ => ScreenCorner.None,
        };
    }

    /// <summary>
    /// Reports a pointer position. Leaving the corner resets it, so the next entry can fire again.
    /// </summary>
    /// <param name="box">The box of the output that the pointer is on, in layout coordinates.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="true"/> once, when the pointer has stayed in the corner for <see cref="DelayMs"/>.</returns>
    public bool Motion(in Box box, double x, double y, uint timeMs)
    {
        var inside = Corner != ScreenCorner.None && x >= box.X && y >= box.Y && x <= box.Right && y <= box.Bottom &&
            At(box, x, y, Size) == Corner;
        if (!inside)
        {
            Reset();
            return false;
        }

        if (!_inside)
        {
            _inside = true;
            _fired = false;
            _enteredMs = timeMs;
        }

        return Poll(timeMs);
    }

    /// <summary>
    /// Checks the delay with no motion, for a timer that fires at <see cref="DueMs"/>.
    /// </summary>
    /// <param name="timeMs">The current time in milliseconds.</param>
    /// <returns><see langword="true"/> once, when the armed corner is due.</returns>
    public bool Poll(uint timeMs)
    {
        if (!_inside || _fired || timeMs - _enteredMs < DelayMs)
        {
            return false;
        }

        _fired = true;
        return true;
    }

    /// <summary>
    /// Disarms the corner as if the pointer left it.
    /// </summary>
    public void Reset()
    {
        _inside = false;
        _fired = false;
    }
}
