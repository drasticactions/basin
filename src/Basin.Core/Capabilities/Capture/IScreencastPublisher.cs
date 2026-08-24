namespace Basin.Capabilities;

public interface IScreencastPublisher
{
    bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info);

    void Close(ulong streamId);
}
