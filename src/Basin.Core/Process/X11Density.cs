namespace Basin;

/// <summary>
/// Selects the Xwayland server that an X11 client uses.
/// </summary>
public enum X11Density
{
    /// <summary>
    /// The Xwayland server that runs at the output scale.
    /// </summary>
    Native,
    /// <summary>
    /// The Xwayland server that runs at scale 1, for clients that do not handle a high density.
    /// </summary>
    Low,
}
