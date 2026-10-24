using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The parsed 16-byte header that opens a waypipe channel. <see cref="WaypipeWire.ParseConnectionHeader"/> makes it.
/// </summary>
/// <param name="Version">The wire version to answer with: the lower of the peer version and <see cref="WaypipeWire.ProtocolVersion"/>.</param>
/// <param name="Compression">The compression that the peer applies to buffer contents.</param>
/// <param name="VideoCodec">The video codec that the peer will send, or <see cref="WaypipeVideoCodec.None"/>.</param>
/// <param name="RefusesDmabuf">A value that is <see langword="true"/> when the peer set the flag that turns off dmabuf transfer.</param>
public readonly record struct WaypipeConnectionHeader(
    uint Version, WaypipeCompression Compression, WaypipeVideoCodec VideoCodec, bool RefusesDmabuf);
