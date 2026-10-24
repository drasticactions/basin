namespace Basin.Audio.PipeWire;

/// <summary>
/// Describes one <c>Stream/Output/Audio</c> node that <see cref="AudioStreams"/> reports. Each key comes from the node first and then from its client.
/// </summary>
public sealed class AudioStream
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioStream"/> class.
    /// </summary>
    /// <param name="nodeId">The PipeWire global id of the stream node.</param>
    public AudioStream(uint nodeId)
    {
        NodeId = nodeId;
    }

    /// <summary>
    /// Gets the PipeWire global id of the stream node. A <see cref="SpatialVoice"/> attaches a stream by this id.
    /// </summary>
    public uint NodeId { get; }

    /// <summary>
    /// Gets the <c>object.serial</c> of the node, or 0 when it is absent.
    /// </summary>
    public ulong Serial { get; init; }

    /// <summary>
    /// Gets the id of the PipeWire client that owns the node, or 0 when it is absent.
    /// </summary>
    public uint ClientId { get; init; }

    /// <summary>
    /// Gets the <c>pipewire.sec.pid</c> of the client, which the daemon reads from the socket. For a pipewire-pulse stream it is the PID of pipewire-pulse. It is 0 when absent.
    /// </summary>
    public int VerifiedPid { get; init; }

    /// <summary>
    /// Gets the <c>application.process.id</c> that the application reports. Nothing checks it. It is 0 when absent.
    /// </summary>
    public int ReportedPid { get; init; }

    /// <summary>
    /// Gets the <c>pipewire.access.portal.app_id</c> of a sandboxed application, or <see langword="null"/>.
    /// </summary>
    public string? FlatpakAppId { get; init; }

    /// <summary>
    /// Gets the <c>application.name</c> of the stream, or <see langword="null"/>.
    /// </summary>
    public string? ApplicationName { get; init; }

    /// <summary>
    /// Gets the <c>media.role</c> of the stream, for example Music or Game, or <see langword="null"/>.
    /// </summary>
    public string? MediaRole { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stream came through pipewire-pulse, from <c>client.api</c>.
    /// </summary>
    public bool ViaPulse { get; init; }

    /// <summary>
    /// Gets the launch tag in <see cref="AudioStreams.LaunchKey"/>, or 0 when the stream has none.
    /// </summary>
    public long LaunchId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the node is in the running state, so it moves samples now. A paused stream that stays open is not running.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Gets a display name: the application name, or <c>node</c> and the node id when the stream has none.
    /// </summary>
    public string Name => ApplicationName is { Length: > 0 } name ? name : $"node{NodeId}";
}
