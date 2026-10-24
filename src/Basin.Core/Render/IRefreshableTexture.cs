using Pixman;

namespace Basin;

/// <summary>
/// Represents a texture that copies its buffer and must be told when the buffer content changed.
/// </summary>
public interface IRefreshableTexture
{
    /// <summary>
    /// Tells the texture that the whole buffer changed. The next pass reads the new content.
    /// </summary>
    void MarkDirty();

    /// <summary>
    /// Tells the texture that the buffer changed only inside a box. Use it only when the texture holds the exact content that the damage is written against. The default marks the whole buffer.
    /// </summary>
    /// <param name="damage">The changed box in buffer coordinates.</param>
    void MarkDirty(in Box damage) => MarkDirty();

    /// <summary>
    /// Points the texture at another buffer of the same size and format and copies only the damaged part from it.
    /// </summary>
    /// <param name="source">The new buffer.</param>
    /// <param name="damage">The part of the new buffer that differs from the content of the texture.</param>
    /// <returns><see langword="false"/> when the texture cannot adopt the buffer, which is the default.</returns>
    bool TryAdopt(IBuffer source, in Box damage) => false;
}
