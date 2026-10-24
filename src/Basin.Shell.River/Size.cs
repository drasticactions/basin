using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// A width and a height in logical pixels.
/// </summary>
/// <param name="Width">The horizontal extent in logical pixels.</param>
/// <param name="Height">The vertical extent in logical pixels.</param>
public readonly record struct Size(int Width, int Height);
