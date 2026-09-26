using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcLockObserver(Action changed) : ILockStateObserver
{
    public void SessionLocked() => changed();

    public void SessionUnlocked() => changed();
}
