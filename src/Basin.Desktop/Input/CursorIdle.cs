namespace Basin.Desktop;

public sealed class CursorIdle : IDisposable
{
    private readonly CursorController _cursor;
    private readonly Basin.Seat.SeatPointer _pointer;
    private readonly IEventSource _timer;
    private int _timeoutMs;
    private bool _hiddenByIdle;

    public CursorIdle(ICompositorEventLoop loop, CursorController cursor, Basin.Seat.SeatPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(pointer);
        _cursor = cursor;
        _pointer = pointer;
        _timer = loop.AddTimer(OnIdle);
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set
        {
            _timeoutMs = Math.Max(0, value);
            NotifyActivity();
        }
    }

    public bool ParkPointer { get; set; }

    public bool IsIdle => _hiddenByIdle;

    public event Action<bool>? IdleChanged;

    public void NotifyActivity()
    {
        if (_hiddenByIdle)
        {
            _hiddenByIdle = false;
            _cursor.Reveal();
            IdleChanged?.Invoke(false);
        }

        _timer.UpdateTimer(_timeoutMs);
    }

    public void Dispose() => _timer.Remove();

    private void OnIdle()
    {
        if (_timeoutMs <= 0 || _hiddenByIdle || _cursor.IsHidden)
        {
            return;
        }

        _hiddenByIdle = true;
        _cursor.Hide();
        if (ParkPointer)
        {
            _pointer.NotifyClearFocus();
        }

        IdleChanged?.Invoke(true);
    }
}
