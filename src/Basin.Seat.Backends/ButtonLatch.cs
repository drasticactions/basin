namespace Basin.Seat.Backends;

/// <summary>
/// Counts held pointer buttons so that a consumer can choose a button route on the first press and keep it to the last release. It also tracks the buttons sent to the seat.
/// </summary>
public sealed class ButtonLatch
{
    private readonly Basin.Seat.Seat _seat;
    private readonly uint[] _seatButtons = new uint[16];
    private int _seatCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ButtonLatch"/> class.
    /// </summary>
    /// <param name="seat">The seat whose pointer receives the buttons from <see cref="SeatButton"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public ButtonLatch(Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _seat = seat;
    }

    /// <summary>
    /// Gets the number of buttons held now.
    /// </summary>
    public int Held { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the seat holds a button that <see cref="SeatButton"/> pressed.
    /// </summary>
    public bool SeatHolds => _seatCount > 0;

    /// <summary>
    /// Counts one press.
    /// </summary>
    /// <returns><see langword="true"/> for the first press, when the consumer chooses the route.</returns>
    public bool Press() => Held++ == 0;

    /// <summary>
    /// Counts one release. The count does not go below zero. The last release also forgets the seat buttons.
    /// </summary>
    /// <returns><see langword="true"/> when no button is held after the release.</returns>
    public bool Release()
    {
        if (Held > 0)
        {
            Held--;
        }

        if (Held > 0)
        {
            return false;
        }

        _seatCount = 0;
        return true;
    }

    /// <summary>
    /// Sends a button to the seat pointer. A release goes to the seat only when this latch sent the press, so the seat never gets an unmatched release.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void SeatButton(uint timeMs, uint button, bool pressed)
    {
        var index = Array.IndexOf(_seatButtons, button, 0, _seatCount);
        if (pressed)
        {
            if (index < 0 && _seatCount < _seatButtons.Length)
            {
                _seatButtons[_seatCount++] = button;
            }

            _seat.Pointer.NotifyButton(timeMs, button, true);
            return;
        }

        if (index < 0)
        {
            return;
        }

        _seatButtons[index] = _seatButtons[--_seatCount];
        _seat.Pointer.NotifyButton(timeMs, button, false);
    }
}
