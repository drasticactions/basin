namespace Basin.Capabilities;

public interface ILockStateObserver
{
    void SessionLocked();

    void SessionUnlocked();
}
