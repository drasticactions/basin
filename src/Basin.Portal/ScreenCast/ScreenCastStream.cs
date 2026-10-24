namespace Basin.Portal;

/// <summary>
/// One published PipeWire stream of a screen cast session.
/// </summary>
public sealed class ScreenCastStream
{
    internal ScreenCastStream(ulong id, uint nodeId, ulong serial, ScreenCastSource source, IOutput? output, Box layoutBox, string mappingId, bool dmabufOffered)
    {
        Id = id;
        NodeId = nodeId;
        Serial = serial;
        Source = source;
        Output = output;
        LayoutBox = layoutBox;
        MappingId = mappingId;
        DmabufOffered = dmabufOffered;
    }

    /// <summary>
    /// Gets the stream id that <see cref="Basin.Capabilities.IScreencastPublisher"/> knows the stream by.
    /// </summary>
    public ulong Id { get; }

    /// <summary>
    /// Gets the PipeWire node id that the application connects to.
    /// </summary>
    public uint NodeId { get; }

    /// <summary>
    /// Gets the PipeWire object serial of the node.
    /// </summary>
    public ulong Serial { get; }

    /// <summary>
    /// Gets the source that the stream shows.
    /// </summary>
    public ScreenCastSource Source { get; }

    /// <summary>
    /// Gets the output of a monitor stream. It is <see langword="null"/> for a window stream.
    /// </summary>
    public IOutput? Output { get; }

    /// <summary>
    /// Gets the area of the stream in layout coordinates, in logical pixels.
    /// </summary>
    public Box LayoutBox { get; }

    /// <summary>
    /// Gets the id that maps the stream to an EIS region.
    /// </summary>
    public string MappingId { get; }

    /// <summary>
    /// Gets a value indicating whether the stream offered dmabuf buffers.
    /// </summary>
    public bool DmabufOffered { get; }
}
