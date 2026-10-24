namespace Basin.Capabilities;

/// <summary>
/// Describes the result of <see cref="IScreencastPublisher.TryPublish"/>.
/// </summary>
public readonly record struct ScreencastStreamInfo
{
    /// <summary>
    /// Gets the PipeWire node id of the stream, or 0 when the stream did not start.
    /// </summary>
    public required uint NodeId { get; init; }

    /// <summary>
    /// Gets the PipeWire object serial of the node, or 0 when it is not known.
    /// </summary>
    public ulong ObjectSerial { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stream offers dmabuf buffers in addition to shared memory.
    /// </summary>
    public bool DmabufOffered { get; init; }

    /// <summary>
    /// Gets a human-readable reason when the stream did not start, otherwise <see langword="null"/>.
    /// </summary>
    public string? FailureReason { get; init; }
}
