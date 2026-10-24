namespace Basin.Backend.Headless;

/// <summary>
/// A pointer that raises each injected event without change. Times are in milliseconds.
/// </summary>
public sealed class HeadlessPointer
{
    internal HeadlessPointer(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the device name that <see cref="HeadlessBackend.CreatePointer"/> gave this pointer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Occurs on <see cref="InjectMotion"/> with the time and the absolute position.
    /// </summary>
    public event Action<uint, double, double>? Motion;

    /// <summary>
    /// Occurs on <see cref="InjectRelativeMotion"/> with the time and the motion delta.
    /// </summary>
    public event Action<uint, double, double>? RelativeMotion;

    /// <summary>
    /// Occurs on <see cref="InjectButton"/> with the time, the button code and the pressed state.
    /// </summary>
    public event Action<uint, uint, bool>? Button;

    /// <summary>
    /// Occurs on <see cref="InjectAxis"/> with the time and the axis event.
    /// </summary>
    public event Action<uint, PointerAxis>? Axis;

    /// <summary>
    /// Occurs on <see cref="InjectFrame"/>. It ends a group of pointer events.
    /// </summary>
    public event Action? Frame;

    /// <summary>
    /// Raises <see cref="Motion"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The absolute horizontal position.</param>
    /// <param name="y">The absolute vertical position.</param>
    public void InjectMotion(uint timeMs, double x, double y) => Motion?.Invoke(timeMs, x, y);

    /// <summary>
    /// Raises <see cref="RelativeMotion"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal delta.</param>
    /// <param name="dy">The vertical delta.</param>
    public void InjectRelativeMotion(uint timeMs, double dx, double dy) => RelativeMotion?.Invoke(timeMs, dx, dy);

    /// <summary>
    /// Raises <see cref="Button"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The button code, for example an evdev <c>BTN_LEFT</c>.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    public void InjectButton(uint timeMs, uint button, bool pressed) => Button?.Invoke(timeMs, button, pressed);

    /// <summary>
    /// Raises <see cref="Axis"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The scroll axis event.</param>
    public void InjectAxis(uint timeMs, in PointerAxis axis) => Axis?.Invoke(timeMs, axis);

    /// <summary>
    /// Raises <see cref="Frame"/>.
    /// </summary>
    public void InjectFrame() => Frame?.Invoke();
}
