namespace Basin.Seat;

/// <summary>
/// Tracks the one touch contact that drives an emulated pointer. It holds state only and sends no events.
/// </summary>
public sealed class TouchPointerEmulator
{
    private readonly SeatTouch _touch;
    private int _slot = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchPointerEmulator"/> class.
    /// </summary>
    /// <param name="touch">The touch device that tells which surfaces take touch.</param>
    public TouchPointerEmulator(SeatTouch touch) => _touch = touch;

    /// <summary>
    /// Gets the id of the emulated contact, or -1 when there is none.
    /// </summary>
    public int Slot => _slot;

    /// <summary>
    /// Gets a value indicating whether a contact drives the pointer.
    /// </summary>
    public bool Active => _slot >= 0;

    /// <summary>
    /// Takes a contact for emulation.
    /// </summary>
    /// <param name="slot">The contact id. A negative id cannot be taken.</param>
    /// <param name="surface">The surface under the contact, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when a contact is already taken or the surface's client has bound <c>wl_touch</c>.</returns>
    public bool TryClaim(int slot, Surface? surface)
    {
        if (_slot >= 0 || _touch.Accepts(surface))
        {
            return false;
        }

        _slot = slot;
        return true;
    }

    /// <summary>
    /// Checks whether a contact is the emulated one.
    /// </summary>
    /// <param name="slot">The contact id.</param>
    /// <returns><see langword="true"/> when the contact drives the pointer.</returns>
    public bool Owns(int slot) => _slot >= 0 && _slot == slot;

    /// <summary>
    /// Frees the emulated contact when it lifts.
    /// </summary>
    /// <param name="slot">The contact id.</param>
    /// <returns><see langword="false"/> when the contact is not the emulated one.</returns>
    public bool Release(int slot)
    {
        if (!Owns(slot))
        {
            return false;
        }

        _slot = -1;
        return true;
    }

    /// <summary>
    /// Frees the emulated contact, whichever it is.
    /// </summary>
    /// <returns><see langword="false"/> when no contact was taken.</returns>
    public bool Cancel()
    {
        if (_slot < 0)
        {
            return false;
        }

        _slot = -1;
        return true;
    }
}
