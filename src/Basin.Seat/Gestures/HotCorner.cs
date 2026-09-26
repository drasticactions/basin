namespace Basin.Seat;

public sealed class HotCorner
{
    private bool _inside;
    private bool _fired;
    private uint _enteredMs;

    public ScreenCorner Corner { get; set; } = ScreenCorner.TopLeft;

    public double Size { get; set; } = 6;

    public uint DelayMs { get; set; } = 150;

    public bool IsArmed => _inside && !_fired;

    public uint DueMs => _enteredMs + DelayMs;

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

    public bool Poll(uint timeMs)
    {
        if (!_inside || _fired || timeMs - _enteredMs < DelayMs)
        {
            return false;
        }

        _fired = true;
        return true;
    }

    public void Reset()
    {
        _inside = false;
        _fired = false;
    }
}
