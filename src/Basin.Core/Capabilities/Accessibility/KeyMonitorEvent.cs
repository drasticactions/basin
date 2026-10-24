namespace Basin.Capabilities;

/// <summary>
/// One key event as an <see cref="IKeyMonitor"/> sees it.
/// </summary>
/// <param name="TimeMs">The event time in milliseconds.</param>
/// <param name="Keycode">The evdev key code.</param>
/// <param name="Released"><see langword="true"/> for a release, <see langword="false"/> for a press.</param>
/// <param name="Modifiers">The xkb mask of the effective modifiers before the key.</param>
/// <param name="Keysym">The xkb keysym of the key in the current state.</param>
/// <param name="Unichar">The Unicode character of the key, or 0 when it has none.</param>
/// <param name="RepeatDelayMs">The seat's key repeat delay in milliseconds.</param>
public readonly record struct KeyMonitorEvent(uint TimeMs, uint Keycode, bool Released, uint Modifiers, uint Keysym, uint Unichar, uint RepeatDelayMs);
