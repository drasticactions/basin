using Pixman;

namespace Basin;

/// <summary>
/// Holds a premultiplied RGBA color with channels from 0 to 1.
/// </summary>
/// <param name="R">The red channel, premultiplied by alpha.</param>
/// <param name="G">The green channel, premultiplied by alpha.</param>
/// <param name="B">The blue channel, premultiplied by alpha.</param>
/// <param name="A">The alpha channel.</param>
public readonly record struct RenderColor(float R, float G, float B, float A)
{
    /// <summary>
    /// A color with all channels at zero.
    /// </summary>
    public static readonly RenderColor Transparent = new(0, 0, 0, 0);

    /// <summary>
    /// Opaque black.
    /// </summary>
    public static readonly RenderColor Black = new(0, 0, 0, 1);
}
