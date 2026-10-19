namespace Basin.WindowManager.Ipc;

public sealed class WmIpcLoop(IWmEventLoop loop) : ICompositorEventLoop
{
    public int Fd => throw new NotSupportedException("a window manager's loop has no fd of its own");

    public void Dispatch(int timeoutMs) => throw new NotSupportedException("the window manager runs its own loop");

    public IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Source(loop.AddFd(fd, (WmFdReadiness)events, (ready, readiness) => handler(ready, (FdReadiness)readiness)));
    }

    public IEventSource AddTimer(Action handler) => new Source(loop.AddTimer(handler));

    public IEventSource AddSignal(int signalNumber, Action<int> handler) =>
        throw new NotSupportedException("a window manager's loop has no signal sources");

    public void AddIdle(Action handler) => loop.AddIdle(handler);

    public void DispatchIdle()
    {
    }

    public void DeferDestroy(IDisposable victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        loop.AddIdle(victim.Dispose);
    }

    private sealed class Source(IWmEventSource inner) : IEventSource
    {
        public bool IsRemoved => inner.IsRemoved;

        public void Remove() => inner.Remove();

        public void UpdateTimer(int delayMs) => inner.UpdateTimer(delayMs);

        public void UpdateFd(FdReadiness events) => inner.UpdateFd((WmFdReadiness)events);
    }
}
