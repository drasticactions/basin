namespace Basin;

/// <summary>
/// Identifies the kind of display, which sets how far away the viewer sits for the automatic scale.
/// </summary>
public enum OutputClass
{
    /// <summary>
    /// An external monitor at desk distance.
    /// </summary>
    Desktop,
    /// <summary>
    /// The built-in panel of a laptop, read closer than a desktop monitor.
    /// </summary>
    Laptop,
    /// <summary>
    /// The built-in panel of a handheld or a tablet. A backend reports it for every internal connector, and the host promotes it to <see cref="Laptop"/> when the machine has a lid switch.
    /// </summary>
    Handheld,
    /// <summary>
    /// A television read from a distance. No backend reports it, because the automatic scale derives the television rule from the panel height.
    /// </summary>
    Tv,
}
