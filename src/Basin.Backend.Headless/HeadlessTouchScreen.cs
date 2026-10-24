namespace Basin.Backend.Headless;

/// <summary>
/// A touch screen that raises each injected event without change. Times are in milliseconds.
/// </summary>
public sealed class HeadlessTouchScreen
{
    internal HeadlessTouchScreen(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the device name that <see cref="HeadlessBackend.CreateTouchScreen"/> gave this touch screen.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Occurs on <see cref="InjectDown"/> with the time, the slot and the position.
    /// </summary>
    public event Action<uint, int, double, double>? Down;

    /// <summary>
    /// Occurs on <see cref="InjectUp"/> with the time and the slot.
    /// </summary>
    public event Action<uint, int>? Up;

    /// <summary>
    /// Occurs on <see cref="InjectMotion"/> with the time, the slot and the new position.
    /// </summary>
    public event Action<uint, int, double, double>? Motion;

    /// <summary>
    /// Occurs on <see cref="InjectFrame"/>. It ends a group of touch events.
    /// </summary>
    public event Action? Frame;

    /// <summary>
    /// Occurs on <see cref="InjectCancel"/>. It cancels all active touch points.
    /// </summary>
    public event Action? Cancel;

    /// <summary>
    /// Raises <see cref="Down"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="slot">The touch point slot.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    public void InjectDown(uint timeMs, int slot, double x, double y) => Down?.Invoke(timeMs, slot, x, y);

    /// <summary>
    /// Raises <see cref="Up"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="slot">The touch point slot.</param>
    public void InjectUp(uint timeMs, int slot) => Up?.Invoke(timeMs, slot);

    /// <summary>
    /// Raises <see cref="Motion"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="slot">The touch point slot.</param>
    /// <param name="x">The new horizontal position.</param>
    /// <param name="y">The new vertical position.</param>
    public void InjectMotion(uint timeMs, int slot, double x, double y) => Motion?.Invoke(timeMs, slot, x, y);

    /// <summary>
    /// Raises <see cref="Frame"/>.
    /// </summary>
    public void InjectFrame() => Frame?.Invoke();

    /// <summary>
    /// Raises <see cref="Cancel"/>.
    /// </summary>
    public void InjectCancel() => Cancel?.Invoke();
}
