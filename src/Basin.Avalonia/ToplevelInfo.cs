using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

/// <summary>
/// What a shell policy can know about a toplevel before its host window opens.
/// </summary>
/// <param name="Title">The window title.</param>
/// <param name="AppId">The application id.</param>
/// <param name="Width">The width in logical pixels.</param>
/// <param name="Height">The height in logical pixels.</param>
/// <param name="Client">The Wayland client, or <see langword="null"/> for a foreign toplevel.</param>
public sealed record ToplevelInfo(
    string Title, string AppId, int Width, int Height, Wayland.Server.WlClient? Client = null);
