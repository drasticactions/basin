namespace Basin;

/// <summary>
/// Allocates buffers in the formats and modifiers that it advertises. Disposing it does not destroy the buffers that it allocated.
/// </summary>
public interface IAllocator : IDisposable
{
    /// <summary>
    /// Gets the formats and modifiers that <see cref="Allocate"/> can produce.
    /// </summary>
    DrmFormatSet Formats { get; }

    /// <summary>
    /// Allocates a buffer. The caller owns the result and must call <see cref="BufferBase.Destroy"/> or the owner's equivalent when it is done.
    /// </summary>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="modifiers">The modifiers that the caller accepts. An empty span lets the allocator choose.</param>
    /// <param name="use">The uses that the buffer must support.</param>
    /// <returns>The new buffer, or <see langword="null"/> when the allocator cannot satisfy the request.</returns>
    IBuffer? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use);
}
