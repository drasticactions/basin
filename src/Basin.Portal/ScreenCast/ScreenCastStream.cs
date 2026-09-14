namespace Basin.Portal;

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

    public ulong Id { get; }

    public uint NodeId { get; }

    public ulong Serial { get; }

    public ScreenCastSource Source { get; }

    public IOutput? Output { get; }

    public Box LayoutBox { get; }

    public string MappingId { get; }

    public bool DmabufOffered { get; }
}
