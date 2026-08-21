namespace Basin.WindowManager;

public interface IWmEventSource
{
    bool IsRemoved { get; }

    void Remove();

    void UpdateTimer(int delayMs);

    void UpdateFd(WmFdReadiness events);
}
