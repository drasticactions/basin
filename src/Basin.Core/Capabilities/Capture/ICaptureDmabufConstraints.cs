namespace Basin.Capabilities;

/// <summary>
/// Gives the dmabuf formats and the render device that capture protocols advertise for destination buffers. Without it, capture protocols advertise shared memory only.
/// </summary>
public interface ICaptureDmabufConstraints
{
    /// <summary>
    /// Gets the device number of the render device that destination dmabufs must come from.
    /// </summary>
    /// <param name="device">The device number, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the device is known, otherwise <see langword="false"/>.</returns>
    bool TryDevice(out ulong device);

    /// <summary>
    /// Gets the formats and modifiers to advertise for destination dmabufs.
    /// </summary>
    DrmFormatSet Formats { get; }

    /// <summary>
    /// Gets a value that tells whether a client dmabuf can be a capture destination.
    /// </summary>
    /// <param name="format">The pixel format of the buffer.</param>
    /// <param name="modifier">The format modifier of the buffer.</param>
    /// <returns><see langword="true"/> when the pair is acceptable. The default accepts only the pairs in <see cref="Formats"/>.</returns>
    bool Wraps(DrmFormat format, ulong modifier) => Formats.Contains(format, modifier);
}
