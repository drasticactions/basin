namespace Basin.Desktop;

/// <summary>
/// Hides the cursor after a period with no pointer activity and shows it again on the next activity. The consumer reports activity through <see cref="NotifyActivity"/>.
/// </summary>
public sealed class CursorIdle : IDisposable
{
    private readonly CursorController _cursor;
    private readonly Basin.Seat.SeatPointer _pointer;
    private readonly IEventSource _timer;
    private int _timeoutMs;
    private bool _hiddenByIdle;

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorIdle"/> class. The timer stays off until <see cref="TimeoutMs"/> is set to a positive value.
    /// </summary>
    /// <param name="loop">The loop that runs the idle timer.</param>
    /// <param name="cursor">The cursor to hide and reveal.</param>
    /// <param name="pointer">The pointer whose focus <see cref="ParkPointer"/> clears.</param>
    public CursorIdle(ICompositorEventLoop loop, CursorController cursor, Basin.Seat.SeatPointer pointer)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(pointer);
        _cursor = cursor;
        _pointer = pointer;
        _timer = loop.AddTimer(OnIdle);
    }

    /// <summary>
    /// Gets or sets the idle time in milliseconds before the cursor hides. Zero turns the feature off. A negative value becomes zero, and each set restarts the timer.
    /// </summary>
    public int TimeoutMs
    {
        get => _timeoutMs;
        set
        {
            _timeoutMs = Math.Max(0, value);
            NotifyActivity();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether hiding the cursor also clears the pointer focus, so that clients stop showing hover state.
    /// </summary>
    public bool ParkPointer { get; set; }

    /// <summary>
    /// Gets a value indicating whether this instance hid the cursor. It is <see langword="false"/> when the cursor is hidden for a different cause.
    /// </summary>
    public bool IsIdle => _hiddenByIdle;

    /// <summary>
    /// Occurs with <see langword="true"/> when the idle timer hides the cursor, and with <see langword="false"/> when activity shows it again.
    /// </summary>
    public event Action<bool>? IdleChanged;

    /// <summary>
    /// Reports pointer activity. It shows the cursor if this instance hid it, and restarts the idle timer.
    /// </summary>
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

    /// <summary>
    /// Removes the idle timer. It does not show a hidden cursor.
    /// </summary>
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
