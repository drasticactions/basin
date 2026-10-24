namespace Basin.Hosted;

/// <summary>
/// One input event that a host view delivers to the compositor thread. The meaning of each field depends on <paramref name="Kind"/>.
/// </summary>
/// <param name="Kind">The kind of event, which selects the fields that carry data.</param>
/// <param name="TimeMs">The event time in milliseconds, in the host's clock.</param>
/// <param name="X">The horizontal position in view-local logical pixels. For <see cref="BasinViewInputKind.PointerRelative"/>, it is the unaccelerated horizontal delta.</param>
/// <param name="Y">The vertical position in view-local logical pixels. For <see cref="BasinViewInputKind.PointerRelative"/>, it is the unaccelerated vertical delta.</param>
/// <param name="Code">The evdev button code for a button event, or the evdev key code for a key event.</param>
/// <param name="Pressed">For a button or key event, <see langword="true"/> on press. For <see cref="BasinViewInputKind.GesturePinchEnd"/>, <see langword="true"/> when the gesture was canceled.</param>
/// <param name="DeltaX">The horizontal scroll in wheel steps, the relative motion delta, or the pinch scale for a pinch update.</param>
/// <param name="DeltaY">The vertical scroll in wheel steps, or the relative motion delta.</param>
/// <param name="TouchId">The touch point identifier for a touch event.</param>
public readonly record struct BasinViewInput(
    BasinViewInputKind Kind,
    uint TimeMs,
    double X,
    double Y,
    uint Code,
    bool Pressed,
    double DeltaX,
    double DeltaY,
    int TouchId);
