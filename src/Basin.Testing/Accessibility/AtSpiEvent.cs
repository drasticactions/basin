namespace Basin.Testing;

/// <summary>
/// One org.a11y.atspi.Event.Object signal that a client received.
/// </summary>
/// <param name="Bus">The unique bus name of the sender.</param>
/// <param name="Path">The object path of the source object.</param>
/// <param name="Member">The signal name, for example "StateChanged" or "Announcement".</param>
/// <param name="Kind">The event detail, for example "focused" for a state change.</param>
/// <param name="Detail1">The first integer detail.</param>
/// <param name="Detail2">The second integer detail.</param>
/// <param name="Text">The string payload, or <see langword="null"/> when the payload is not a string.</param>
public sealed record AtSpiEvent(string Bus, string Path, string Member, string Kind, int Detail1, int Detail2, string? Text);
