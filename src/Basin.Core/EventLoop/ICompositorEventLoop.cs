namespace Basin;

public interface ICompositorEventLoop
{
    void Dispatch(int timeoutMs);

    IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler);

    IEventSource AddTimer(Action handler);

    IEventSource AddSignal(int signalNumber, Action<int> handler);

    void AddIdle(Action handler);

    void DispatchIdle();

    int Fd { get; }

    void DeferDestroy(IDisposable victim);
}
