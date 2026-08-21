using Basin.Capabilities;

namespace Basin.Transport.Waypipe;

public sealed record WaypipeChannelOptions
{
    public bool CarriesDmabuf { get; init; }

    public bool AcceptsVideo { get; init; }

    public IVideoDecoder? VideoDecoder { get; init; }
}
