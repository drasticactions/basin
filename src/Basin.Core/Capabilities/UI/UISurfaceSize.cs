using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// The logical size and the scale of an <see cref="IUISurface"/>.
/// </summary>
/// <param name="Width">The width in logical pixels.</param>
/// <param name="Height">The height in logical pixels.</param>
/// <param name="Scale">The scale from logical to buffer pixels.</param>
public readonly record struct UISurfaceSize(int Width, int Height, double Scale);
