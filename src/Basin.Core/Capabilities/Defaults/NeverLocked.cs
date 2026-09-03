namespace Basin.Capabilities.Defaults;

public sealed class NeverLocked : ILockState
{
    public static NeverLocked Instance { get; } = new();

    public bool IsLocked => false;

    public void AddObserver(ILockStateObserver observer) => ArgumentNullException.ThrowIfNull(observer);

    public void RemoveObserver(ILockStateObserver observer) => ArgumentNullException.ThrowIfNull(observer);
}
