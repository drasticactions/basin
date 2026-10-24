namespace Basin.Seat;

/// <summary>
/// Emulates a left mouse button from one touch contact on a surface whose client has not bound <c>wl_touch</c>.
/// </summary>
public sealed class TouchPointerDriver
{
    private const uint BtnLeft = 0x110;

    private readonly TouchPointerEmulator _emulator;
    private readonly ITouchPointerTarget _target;

    /// <summary>
    /// Initializes a new instance of the <see cref="TouchPointerDriver"/> class.
    /// </summary>
    /// <param name="touch">The touch device that tells which surfaces take touch.</param>
    /// <param name="target">The compositor's pointer path that receives the emulated events.</param>
    public TouchPointerDriver(SeatTouch touch, ITouchPointerTarget target)
    {
        ArgumentNullException.ThrowIfNull(touch);
        ArgumentNullException.ThrowIfNull(target);

        _emulator = new TouchPointerEmulator(touch);
        _target = target;
    }

    /// <summary>
    /// Gets or sets a value indicating whether a contact on no surface also drives the pointer, for example on the desktop background.
    /// </summary>
    public bool ClaimWithoutSurface { get; set; }

    /// <summary>
    /// Gets a value indicating whether a contact drives the pointer now.
    /// </summary>
    public bool Active => _emulator.Active;

    /// <summary>
    /// Checks whether a contact drives the pointer.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="true"/> when the contact is the emulated one.</returns>
    public bool Owns(int id) => _emulator.Owns(id);

    /// <summary>
    /// Takes a contact for emulation, then moves the pointer to it and presses the left button.
    /// </summary>
    /// <param name="id">The contact id. It must not be negative.</param>
    /// <param name="surface">The surface under the contact, or <see langword="null"/>.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="false"/> when another contact drives the pointer, the surface takes touch, or there is no surface and <see cref="ClaimWithoutSurface"/> is off.</returns>
    public bool TryClaim(int id, Surface? surface, uint timeMs, double x, double y)
    {
        if (surface is null && !ClaimWithoutSurface)
        {
            return false;
        }

        if (!_emulator.TryClaim(id, surface))
        {
            return false;
        }

        _target.Warp(timeMs, x, y);
        _target.Button(timeMs, BtnLeft, pressed: true);
        return true;
    }

    /// <summary>
    /// Moves the pointer with the emulated contact.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <returns><see langword="false"/> when the contact is not the emulated one.</returns>
    public bool Motion(int id, uint timeMs, double x, double y)
    {
        if (!_emulator.Owns(id))
        {
            return false;
        }

        _target.Warp(timeMs, x, y);
        return true;
    }

    /// <summary>
    /// Releases the left button when the emulated contact lifts.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <returns><see langword="false"/> when the contact is not the emulated one.</returns>
    public bool Release(int id, uint timeMs)
    {
        if (!_emulator.Release(id))
        {
            return false;
        }

        _target.Button(timeMs, BtnLeft, pressed: false);
        return true;
    }

    /// <summary>
    /// Ends the emulation and releases the left button, with the time from <see cref="Environment.TickCount"/>.
    /// </summary>
    /// <returns><see langword="false"/> when no contact drove the pointer.</returns>
    public bool Cancel()
    {
        if (!_emulator.Cancel())
        {
            return false;
        }

        _target.Button((uint)Environment.TickCount, BtnLeft, pressed: false);
        return true;
    }
}
