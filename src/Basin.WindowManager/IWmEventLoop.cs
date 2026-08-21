namespace Basin.WindowManager;

public interface IWmEventLoop
{
    IWmEventSource AddFd(int fd, WmFdReadiness events, Action<int, WmFdReadiness> handler);

    IWmEventSource AddTimer(Action handler);

    void AddIdle(Action handler);
}
