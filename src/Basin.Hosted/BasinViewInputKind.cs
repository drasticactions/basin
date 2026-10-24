namespace Basin.Hosted;

/// <summary>
/// The kinds of <see cref="BasinViewInput"/> event that a host view can deliver.
/// </summary>
public enum BasinViewInputKind : byte
{
    /// <summary>
    /// The pointer moved to <see cref="BasinViewInput.X"/> and <see cref="BasinViewInput.Y"/> inside the view.
    /// </summary>
    PointerMotion,
    /// <summary>
    /// The pointer entered the view at <see cref="BasinViewInput.X"/> and <see cref="BasinViewInput.Y"/>.
    /// </summary>
    PointerEnter,
    /// <summary>
    /// The pointer left the view.
    /// </summary>
    PointerLeave,
    /// <summary>
    /// A pointer button changed state. <see cref="BasinViewInput.Code"/> is the evdev button code.
    /// </summary>
    PointerButton,
    /// <summary>
    /// The pointer scrolled by <see cref="BasinViewInput.DeltaX"/> and <see cref="BasinViewInput.DeltaY"/> wheel steps.
    /// </summary>
    PointerAxis,
    /// <summary>
    /// A key changed state. <see cref="BasinViewInput.Code"/> is the evdev key code.
    /// </summary>
    Key,
    /// <summary>
    /// A touch point with <see cref="BasinViewInput.TouchId"/> touched the view.
    /// </summary>
    TouchDown,
    /// <summary>
    /// A touch point moved.
    /// </summary>
    TouchMotion,
    /// <summary>
    /// A touch point lifted from the view.
    /// </summary>
    TouchUp,
    /// <summary>
    /// The host canceled all touch points of the view.
    /// </summary>
    TouchCancel,
    /// <summary>
    /// The view received keyboard focus from the host.
    /// </summary>
    FocusIn,
    /// <summary>
    /// The view lost keyboard focus to the host.
    /// </summary>
    FocusOut,
    /// <summary>
    /// The pointer moved by a relative delta with no absolute position, for example while the pointer is locked.
    /// </summary>
    PointerRelative,
    /// <summary>
    /// A two-finger pinch gesture started.
    /// </summary>
    GesturePinchBegin,
    /// <summary>
    /// A pinch gesture changed. <see cref="BasinViewInput.DeltaX"/> is the scale since the gesture began.
    /// </summary>
    GesturePinchUpdate,
    /// <summary>
    /// A pinch gesture ended. <see cref="BasinViewInput.Pressed"/> is <see langword="true"/> when it was canceled.
    /// </summary>
    GesturePinchEnd,
}
