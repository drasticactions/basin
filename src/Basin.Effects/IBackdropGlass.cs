using Basin.Capabilities;

namespace Basin.Effects;

/// <summary>
/// Represents a renderer-specific glass effect that refracts and blurs the pixels under a frame shape.
/// </summary>
public interface IBackdropGlass : IShapedBackdropEffect, IDisposable
{
    /// <summary>
    /// Gets or sets the glass options that apply to every key.
    /// </summary>
    GlassOptions Options { get; set; }

    /// <summary>
    /// Gets the distance, in pixels, that the glass reads outside a region. A caller must damage that much more around a glass region.
    /// </summary>
    int ExpandSize { get; }
}
