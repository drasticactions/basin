using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// The default shell policy. Avalonia places the windows, layer surfaces go to the default screen, and a close request closes the toplevel.
/// </summary>
public sealed class AvaloniaShellPolicy : IAvaloniaShellPolicy
{
    /// <summary>
    /// Does nothing, so Avalonia places the window.
    /// </summary>
    /// <param name="window">The host window.</param>
    /// <param name="info">The toplevel that the window shows.</param>
    public void PlaceWindow(Window window, ToplevelInfo info)
    {
    }

    /// <summary>
    /// Chooses no screen, so layer surfaces go to the default screen.
    /// </summary>
    /// <param name="screens">The current host screens.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens) => null;

    /// <summary>
    /// Sends a close event to the toplevel on each request.
    /// </summary>
    /// <param name="toplevel">The toplevel of the window.</param>
    /// <param name="requests">The number of close requests so far. It is not used.</param>
    public void CloseRequested(XdgToplevelWindow toplevel, int requests)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        toplevel.Close();
    }
}
