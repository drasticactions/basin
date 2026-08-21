using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public readonly record struct WaypipeConnectionHeader(
    uint Version, WaypipeCompression Compression, WaypipeVideoCodec VideoCodec, bool RefusesDmabuf);
