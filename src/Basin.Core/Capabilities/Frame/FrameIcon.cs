namespace Basin.Capabilities;

/// <summary>
/// The icon that a frame can draw for a window. Either field can be absent.
/// </summary>
/// <param name="Name">An icon name to look up in the icon theme, or <see langword="null"/>.</param>
/// <param name="Pixels">A buffer that holds the icon pixels the client sent, or <see langword="null"/>.</param>
public readonly record struct FrameIcon(string? Name, IBuffer? Pixels);
