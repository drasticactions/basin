using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Selects the shape of an <see cref="OpenCloseAnimation"/>.
/// </summary>
public enum OpenCloseKind
{
    /// <summary>
    /// Changes only the opacity.
    /// </summary>
    Fade,

    /// <summary>
    /// Changes the opacity and scales the window about its center from the rest scale.
    /// </summary>
    Zoom,
}
