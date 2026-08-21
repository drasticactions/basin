namespace Basin.Seat;

public readonly record struct HostKeyEvent(uint TimeMs, HostKeyCode Code, bool Pressed);
