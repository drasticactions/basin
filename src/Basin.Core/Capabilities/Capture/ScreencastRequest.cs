namespace Basin.Capabilities;

/// <summary>
/// Describes a stream that an <see cref="IScreencastPublisher"/> must start.
/// </summary>
public readonly record struct ScreencastRequest
{
    /// <summary>
    /// Gets the caller's id for the stream. <see cref="IScreencastPublisher.Close"/> takes the same id.
    /// </summary>
    public required ulong StreamId { get; init; }

    /// <summary>
    /// Gets the source that the stream captures.
    /// </summary>
    public required CaptureSource Source { get; init; }

    /// <summary>
    /// Gets the cursor mode of the stream. It holds one flag.
    /// </summary>
    public ScreencastCursorMode Cursor { get; init; }

    /// <summary>
    /// Gets an optional zero-copy source of output frames, or <see langword="null"/> for none.
    /// </summary>
    public IDmabufCapture? Dmabuf { get; init; }
}
