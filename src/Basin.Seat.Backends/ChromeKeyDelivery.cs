using Basin.Scene;

namespace Basin.Seat.Backends;

/// <summary>
/// Offers each key to the compositor UI first and sends the key to the seat only when no UI surface takes it.
/// </summary>
public sealed class ChromeKeyDelivery
{
    private readonly Basin.Seat.Seat _seat;
    private readonly UISurfaceRouter _router;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromeKeyDelivery"/> class.
    /// </summary>
    /// <param name="seat">The seat that gets the keys that the UI does not take.</param>
    /// <param name="router">The router that delivers keys to the focused UI surface.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="router"/> is <see langword="null"/>.</exception>
    public ChromeKeyDelivery(Basin.Seat.Seat seat, UISurfaceRouter router)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(router);
        _seat = seat;
        _router = router;
    }

    /// <summary>
    /// Gets a value indicating whether a key press that the UI takes also commits its text to the UI surface. The default is <see langword="true"/>.
    /// </summary>
    public bool CommitText { get; init; } = true;

    /// <summary>
    /// Offers the key to the UI through <see cref="Chrome"/>. When the UI does not take it, the key goes to the seat keyboard.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="true"/> when the UI took the key.</returns>
    public bool Deliver(uint timeMs, uint key, bool pressed)
    {
        if (!Chrome(timeMs, key, pressed))
        {
            _seat.Keyboard.NotifyKey(timeMs, key, pressed);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Offers the key to the UI only. When the UI takes it, the seat marks the key consumed so that no client sees it.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="true"/> when the UI took the key.</returns>
    public bool Chrome(uint timeMs, uint key, bool pressed)
    {
        if (!_router.Key(timeMs, key, pressed))
        {
            return false;
        }

        if (pressed && CommitText)
        {
            Span<char> text = stackalloc char[16];
            var length = _seat.Keyboard.TextFor(key, text);
            if (length > 0)
            {
                _router.TextCommit(text[..length]);
            }
        }

        _seat.Keyboard.NotifyKeyConsumed(key, pressed);
        return true;
    }
}
