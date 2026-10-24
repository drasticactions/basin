namespace Basin.Seat;

/// <summary>
/// Tracks the one touch contact that drives an emulated pointer. It holds state only and sends no events.
/// </summary>
public sealed class TouchPointerEmulator
{
    private readonly SeatTouch _touch;
    private int _id = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchPointerEmulator"/> class.
    /// </summary>
    /// <param name="touch">The touch device that tells which surfaces take touch.</param>
    public TouchPointerEmulator(SeatTouch touch) => _touch = touch;

    /// <summary>
    /// Gets the id of the emulated contact, or -1 when there is none.
    /// </summary>
    public int ContactId => _id;

    /// <summary>
    /// Gets a value indicating whether a contact drives the pointer.
    /// </summary>
    public bool Active => _id >= 0;

    /// <summary>
    /// Takes a contact for emulation.
    /// </summary>
    /// <param name="id">The contact id. A negative id cannot be taken.</param>
    /// <param name="surface">The surface under the contact, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the id is negative, a contact is already taken, or the surface's client has bound <c>wl_touch</c>.</returns>
    public bool TryClaim(int id, Surface? surface)
    {
        if (id < 0 || _id >= 0 || _touch.Accepts(surface))
        {
            return false;
        }

        _id = id;
        return true;
    }

    /// <summary>
    /// Checks whether a contact is the emulated one.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="true"/> when the contact drives the pointer.</returns>
    public bool Owns(int id) => _id >= 0 && _id == id;

    /// <summary>
    /// Frees the emulated contact when it lifts.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="false"/> when the contact is not the emulated one.</returns>
    public bool Release(int id)
    {
        if (!Owns(id))
        {
            return false;
        }

        _id = -1;
        return true;
    }

    /// <summary>
    /// Frees the emulated contact, whichever it is.
    /// </summary>
    /// <returns><see langword="false"/> when no contact was taken.</returns>
    public bool Cancel()
    {
        if (_id < 0)
        {
            return false;
        }

        _id = -1;
        return true;
    }
}
