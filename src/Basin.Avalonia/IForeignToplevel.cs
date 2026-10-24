using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

/// <summary>
/// A window from a shell other than xdg-shell, for example an X11 window, that <see cref="ToplevelWindows.AddForeign"/> shows in a host window.
/// </summary>
public interface IForeignToplevel
{
    /// <summary>
    /// Gets the surface that shows the window content.
    /// </summary>
    Surface Surface { get; }

    /// <summary>
    /// Gets the window title.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the application id, or an empty string.
    /// </summary>
    string AppId { get; }

    /// <summary>
    /// Gets the width in logical pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height in logical pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the host window must draw a frame for the window.
    /// </summary>
    bool ServerDecorated { get; }

    /// <summary>
    /// Gets a value indicating whether the window is a popup that is placed against <see cref="AnchorSurface"/>.
    /// </summary>
    bool IsPopup { get; }

    /// <summary>
    /// Gets the surface that a popup is placed against, or <see langword="null"/> to use the first host window.
    /// </summary>
    Surface? AnchorSurface { get; }

    /// <summary>
    /// Gets the x offset of a popup from its anchor surface, in logical pixels.
    /// </summary>
    int AnchorOffsetX { get; }

    /// <summary>
    /// Gets the y offset of a popup from its anchor surface, in logical pixels.
    /// </summary>
    int AnchorOffsetY { get; }

    /// <summary>
    /// Occurs when <see cref="Title"/> changes.
    /// </summary>
    event Action? TitleChanged;

    /// <summary>
    /// Occurs when the size or the anchor offset changes.
    /// </summary>
    event Action? GeometryChanged;

    /// <summary>
    /// Occurs when the window is gone. The host window then closes.
    /// </summary>
    event Action? Closed;

    /// <summary>
    /// Asks the window to take a new size after the host window was resized.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    void Resize(int width, int height);

    /// <summary>
    /// Asks the window to close, after a click on the close button of the host window.
    /// </summary>
    void Close();

    /// <summary>
    /// Tells the window that its host window gained or lost activation.
    /// </summary>
    /// <param name="active"><see langword="true"/> when the host window is active.</param>
    void Activate(bool active);
}
