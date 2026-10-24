namespace Basin.Capabilities;

/// <summary>
/// A decode session that finishes frames after <see cref="IVideoDecodeSession.Decode"/> returns. Its
/// <see cref="IVideoDecodeSession.Decode"/> result only says whether the packet was queued.
/// </summary>
public interface IAsyncVideoDecodeSession : IVideoDecodeSession
{
    /// <summary>
    /// Occurs once for each queued packet, in queue order. The arguments are the destination that
    /// <see cref="IVideoDecodeSession.Decode"/> was given and whether a frame was written there.
    /// </summary>
    event Action<nint, bool>? Completed;
}
