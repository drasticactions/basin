using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// The window policy of <see cref="ToplevelWindows"/>: where host windows go, which screen gets layer surfaces and how close requests are handled.
/// </summary>
public interface IAvaloniaShellPolicy
{
    /// <summary>
    /// Places a new host window before it is shown. It is called on the UI thread.
    /// </summary>
    /// <param name="window">The host window. It is not shown yet.</param>
    /// <param name="info">The toplevel that the window shows.</param>
    void PlaceWindow(Window window, ToplevelInfo info);

    /// <summary>
    /// Chooses the screen for a layer surface that did not name an output. It is called on the compositor thread.
    /// </summary>
    /// <param name="screens">The current host screens.</param>
    /// <returns>The key of a screen, or <see langword="null"/> for the default screen.</returns>
    string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens);

    /// <summary>
    /// Handles a click on the close button of a host window. It is called on the compositor thread.
    /// </summary>
    /// <param name="toplevel">The toplevel of the window.</param>
    /// <param name="requests">The number of close requests for this toplevel so far, starting at 1.</param>
    void CloseRequested(XdgToplevelWindow toplevel, int requests);

    /// <summary>
    /// Chooses how a toplevel is shown when it maps for the first time. It is called on the compositor thread.
    /// </summary>
    /// <param name="info">The toplevel, with its geometry size.</param>
    /// <returns>The kind. The default implementation returns <see cref="ScreenSurfaceKind.Application"/>.</returns>
    ScreenSurfaceKind Classify(ToplevelInfo info) => ScreenSurfaceKind.Application;
}
