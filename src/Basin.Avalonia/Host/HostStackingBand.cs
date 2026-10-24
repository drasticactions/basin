using Basin.Shell.Xdg;

namespace Basin.Avalonia;

/// <summary>
/// The stacking level of a host window that shows a layer surface.
/// </summary>
public enum HostStackingBand
{
    /// <summary>Below all other windows, for the background layer.</summary>
    Background,
    /// <summary>Below ordinary windows, for the bottom layer.</summary>
    Below,
    /// <summary>Above ordinary windows, for the top layer.</summary>
    Above,
    /// <summary>Above all other windows, for the overlay layer.</summary>
    Overlay,
}
