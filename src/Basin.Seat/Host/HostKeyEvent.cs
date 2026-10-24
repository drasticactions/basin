namespace Basin.Seat;

/// <summary>
/// A key event from a host toolkit, as a physical key position.
/// </summary>
/// <param name="TimeMs">The event time in milliseconds.</param>
/// <param name="Code">The physical key position.</param>
/// <param name="Pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
public readonly record struct HostKeyEvent(uint TimeMs, HostKeyCode Code, bool Pressed);
