using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// How an output presents frames. The values match the presentation modes of river-window-management-v1.
/// </summary>
public enum PresentationMode
{
    /// <summary>Frames wait for the vertical blank. Tearing cannot occur.</summary>
    Vsync = 0,

    /// <summary>Frames present as soon as they are ready. Tearing can occur.</summary>
    Async = 1,
}
