namespace Basin.Ipc;

public interface IIpcEventSubscriber
{
    void OnEvent(string name, ReadOnlySpan<byte> message);
}
