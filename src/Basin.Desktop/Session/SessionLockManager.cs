using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class SessionLockManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly WlServerDisplay _display;
    private ExtSessionLockV1Resource? _activeLock;

    public SessionLockManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _display = display;
        _compositor = compositor;
        _global = display.CreateGlobal(ExtSessionLockManagerV1.Interface, Version, OnBind);
    }

    public bool IsLocked { get; private set; }

    public event Action? Locked;

    public event Action? Unlocked;

    public event Action? Abandoned;

    public event Action<LockSurface>? NewLockSurface;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtSessionLockManagerV1Resource(client, version, id);
        manager.Lock += (_, e) =>
        {
            var lockResource = new ExtSessionLockV1Resource(client, manager.Version, e.Id);
            if (_activeLock is not null && !_activeLock.IsDestroyed)
            {
                lockResource.SendFinished();
                return;
            }

            _activeLock = lockResource;
            var wasLocked = IsLocked;
            IsLocked = true;
            lockResource.SendLocked();
            if (!wasLocked)
            {
                Locked?.Invoke();
            }

            var unlocked = false;
            lockResource.UnlockAndDestroy += (_, _) =>
            {
                unlocked = true;
                IsLocked = false;
                _activeLock = null;
                Unlocked?.Invoke();
            };
            lockResource.Destroyed += (_, _) =>
            {
                if (!unlocked && ReferenceEquals(_activeLock, lockResource))
                {
                    _activeLock = null;
                    Abandoned?.Invoke();
                }
            };
            lockResource.GetLockSurface += (_, se) =>
            {
                var surface = _compositor.ResolveSurface(se.Surface);
                var output = OutputGlobal.FromResource(se.Output);
                var surfaceResource = new ExtSessionLockSurfaceV1Resource(client, lockResource.Version, se.Id);
                if (surface is null || output is null)
                {
                    return;
                }

                if (!surface.CanSetRole(LockSurface.RoleName))
                {
                    lockResource.PostError((uint)ExtSessionLockV1.Error.Role, "surface already has a role");
                    return;
                }

                var lockSurface = new LockSurface(_display, surface, surfaceResource, output);
                surface.TrySetRole(LockSurface.RoleName, lockSurface);
                NewLockSurface?.Invoke(lockSurface);
            };
        };
    }
}
