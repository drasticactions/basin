namespace Basin.Ipc;

public interface IIpcReplySink
{
    bool IsOpen { get; }

    IpcClientState State { get; }

    void Deliver(IpcReply reply);
}
