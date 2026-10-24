using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The compression of buffer contents on a waypipe channel. Both sides must use the same value, because the transport does not negotiate it.
/// </summary>
public enum WaypipeCompression
{
    /// <summary>
    /// Buffer contents travel uncompressed.
    /// </summary>
    None = 1,

    /// <summary>
    /// Buffer contents travel as LZ4 blocks.
    /// </summary>
    Lz4 = 2,

    /// <summary>
    /// Buffer contents travel as zstd frames.
    /// </summary>
    Zstd = 3,
}
