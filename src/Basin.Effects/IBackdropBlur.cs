using Basin.Capabilities;

namespace Basin.Effects;

/// <summary>
/// Represents a renderer-specific backdrop blur that blurs the pixels under a region. One instance serves every surface, and per-key options override the shared ones.
/// </summary>
public interface IBackdropBlur : IBackdropEffect, IBackgroundEffects, IDisposable
{
    /// <summary>
    /// Gets or sets the options that apply to every key with no override. Setting them can rebuild the blur levels, so a caller must not set them on each frame.
    /// </summary>
    BlurOptions Options { get; set; }

    /// <summary>
    /// Gets or sets the corner radii that apply when a key sets only square corners.
    /// </summary>
    BlurCorners Corners { get; set; }

    /// <summary>
    /// Gets or sets the opacity factor of every blur, from 0 to 1. It multiplies the opacity of each key.
    /// </summary>
    double Opacity { get; set; }

    /// <summary>
    /// Gets the distance, in pixels, that the blur reads outside a region. A caller must damage that much more around a blurred region.
    /// </summary>
    int ExpandSize { get; }

    /// <summary>
    /// Sets the options for the draws with a key. It replaces earlier options for the same key.
    /// </summary>
    /// <param name="key">The key that the draws use.</param>
    /// <param name="options">The options for that key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    void SetSurface(object key, in BlurSurfaceOptions options);
}
