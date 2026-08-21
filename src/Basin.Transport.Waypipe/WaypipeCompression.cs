using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public enum WaypipeCompression
{
    None = 1,

    Lz4 = 2,

    Zstd = 3,
}
