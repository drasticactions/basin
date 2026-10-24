using Pixman;

namespace Basin;

/// <summary>
/// Identifies how a draw combines with the target.
/// </summary>
public enum RenderBlend
{
    /// <summary>
    /// Porter-Duff over with premultiplied alpha.
    /// </summary>
    PremultipliedOver,

    /// <summary>
    /// Adds the source to the target, for glow and flame effects.
    /// </summary>
    Additive,
}
