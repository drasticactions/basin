using Basin.Capabilities;

namespace Basin.Seat;

/// <summary>
/// Forwards synthetic input to <see cref="Seat.SyntheticInput"/>, and drops it while the session is locked.
/// </summary>
public sealed class SeatSyntheticInput : ISyntheticInput
{
    private readonly Seat _seat;
    private readonly BasinServices _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatSyntheticInput"/> class.
    /// </summary>
    /// <param name="seat">The seat whose synthetic input target receives the input.</param>
    /// <param name="services">The services that give the <see cref="ILockState"/>.</param>
    public SeatSyntheticInput(Seat seat, BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(services);
        _seat = seat;
        _services = services;
    }

    /// <summary>
    /// Gets or sets a value indicating whether input goes through while the session is locked.
    /// </summary>
    public bool AllowWhileLocked { get; set; }

    /// <summary>
    /// Gets a value indicating whether the seat has a target and the lock does not block it now.
    /// </summary>
    public bool IsAvailable => Target is not null;

    private ISyntheticInput? Target =>
        _seat.SyntheticInput is { } target && (AllowWhileLocked || _services.Find<ILockState>() is not { IsLocked: true }) ? target : null;

    /// <inheritdoc/>
    public bool PointerMotionAbsolute(uint timeMs, double x, double y) => Target?.PointerMotionAbsolute(timeMs, x, y) ?? false;

    /// <inheritdoc/>
    public bool PointerButton(uint timeMs, uint button, bool pressed) => Target?.PointerButton(timeMs, button, pressed) ?? false;

    /// <inheritdoc/>
    public bool PointerAxis(uint timeMs, uint axis, double value, uint source) => Target?.PointerAxis(timeMs, axis, value, source) ?? false;

    /// <inheritdoc/>
    public bool Key(uint timeMs, uint keycode, bool pressed) => Target?.Key(timeMs, keycode, pressed) ?? false;

    /// <inheritdoc/>
    public bool TouchDown(uint timeMs, int id, double x, double y) => Target?.TouchDown(timeMs, id, x, y) ?? false;

    /// <inheritdoc/>
    public bool TouchMotion(uint timeMs, int id, double x, double y) => Target?.TouchMotion(timeMs, id, x, y) ?? false;

    /// <inheritdoc/>
    public bool TouchUp(uint timeMs, int id) => Target?.TouchUp(timeMs, id) ?? false;

    /// <inheritdoc/>
    public bool TouchFrame() => Target?.TouchFrame() ?? false;

    /// <inheritdoc/>
    public bool TouchCancel() => Target?.TouchCancel() ?? false;

    /// <summary>
    /// Reads the pointer position in layout coordinates from the target. With no target, it gives 0, 0.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns><see langword="false"/> when there is no target or the target cannot tell.</returns>
    public bool TryPointerPosition(out double x, out double y)
    {
        if (Target is { } target)
        {
            return target.TryPointerPosition(out x, out y);
        }

        (x, y) = (0, 0);
        return false;
    }
}
