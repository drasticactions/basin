namespace Basin;

public interface IEventSource
{
    bool IsRemoved { get; }

    void Remove();

    void UpdateTimer(int delayMs);

    void UpdateFd(FdReadiness events);
}
