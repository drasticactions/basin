namespace Basin.Capabilities;

/// <summary>
/// Publishes capture sources as PipeWire video streams. The implementation pulls pixels through <see cref="IScreenCapture"/> on its own cadence.
/// </summary>
public interface IScreencastPublisher
{
    /// <summary>
    /// Starts a stream for a capture source.
    /// </summary>
    /// <param name="request">The stream id, the source and the cursor mode.</param>
    /// <param name="info">The node id and serial of the new stream, or the failure reason when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the stream exists, otherwise <see langword="false"/>.</returns>
    bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info);

    /// <summary>
    /// Stops and destroys a stream. An unknown id is ignored.
    /// </summary>
    /// <param name="streamId">The id that <see cref="ScreencastRequest.StreamId"/> gave.</param>
    void Close(ulong streamId);
}
