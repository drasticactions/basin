namespace Basin.Greetd;

/// <summary>
/// The kind of a <see cref="GreetdSession"/>.
/// </summary>
public enum GreetdSessionType
{
    /// <summary>An entry from <c>wayland-sessions</c>.</summary>
    Wayland,
    /// <summary>An entry from <c>xsessions</c>. It runs through the X11 wrapper.</summary>
    X11,
    /// <summary>A shell command line from the environments file or from the user. It is not quoted.</summary>
    Command,
}
