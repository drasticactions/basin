namespace Basin.Capabilities;

public interface IAsyncVideoDecodeSession : IVideoDecodeSession
{
    event Action<nint, bool>? Completed;
}
