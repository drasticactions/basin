namespace Basin.Capabilities;

public interface ILockState
{
    bool IsLocked { get; }

    void AddObserver(ILockStateObserver observer);

    void RemoveObserver(ILockStateObserver observer);
}
