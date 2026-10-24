using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// The keyboard focus that a layer surface holds on a seat, as the consumer reports it through <see cref="RiverLayerShell.SetLayerFocus(Basin.Seat.Seat, LayerFocus, Surface?)"/>.
/// </summary>
public enum LayerFocus
{
    /// <summary>No layer surface has keyboard focus. The window manager controls focus.</summary>
    None,

    /// <summary>
    /// A layer surface with on-demand keyboard interactivity has focus.
    /// A window manager that focuses a window in the same manage sequence takes the focus back.
    /// </summary>
    NonExclusive,

    /// <summary>
    /// A layer surface with exclusive keyboard interactivity has focus.
    /// The compositor ignores focus requests from the window manager while it holds focus.
    /// </summary>
    Exclusive,
}
