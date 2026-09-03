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
    private readonly OutputLayout? _layout;
    private readonly List<Waiter> _waiters = [];
    private ExtSessionLockV1Resource? _activeLock;

    public SessionLockManager(WlServerDisplay display, CompositorGlobal compositor, OutputLayout? layout = null)
    {
        _display = display;
        _compositor = compositor;
        _layout = layout;
        _global = display.CreateGlobal(ExtSessionLockManagerV1.Interface, Version, OnBind);
    }

    public bool IsLocked { get; private set; }

    public bool IsPresentedLocked { get; private set; }

    public event Action? LockRequested;

    public event Action? Locked;

    public event Action? Unlocked;

    public event Action? Abandoned;

    public event Action<LockSurface>? NewLockSurface;

    public void Dispose()
    {
        CancelWaiters();
        _global.Dispose();
    }

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
            if (!wasLocked)
            {
                LockRequested?.Invoke();
            }

            if (IsPresentedLocked)
            {
                lockResource.SendLocked();
            }
            else
            {
                BeginWaiting();
            }

            var unlocked = false;
            lockResource.UnlockAndDestroy += (_, _) =>
            {
                unlocked = true;
                CancelWaiters();
                IsLocked = false;
                _activeLock = null;
                IsPresentedLocked = false;
                Unlocked?.Invoke();
            };
            lockResource.Destroyed += (_, _) =>
            {
                if (!unlocked && ReferenceEquals(_activeLock, lockResource))
                {
                    CancelWaiters();
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

    private void BeginWaiting()
    {
        CancelWaiters();
        if (_layout is { } layout)
        {
            foreach (var (output, _) in layout.Outputs)
            {
                if (!output.Enabled)
                {
                    continue;
                }

                var waiter = new Waiter(this, output);
                _waiters.Add(waiter);
                output.RequestRepaint();
            }
        }

        if (_waiters.Count == 0)
        {
            Present();
        }
    }

    private void Complete(Waiter waiter)
    {
        waiter.Detach();
        if (_waiters.Remove(waiter) && _waiters.Count == 0)
        {
            Present();
        }
    }

    private void CancelWaiters()
    {
        foreach (var waiter in _waiters)
        {
            waiter.Detach();
        }

        _waiters.Clear();
    }

    private void Present()
    {
        if (IsPresentedLocked || _activeLock is not { IsDestroyed: false } lockResource)
        {
            return;
        }

        IsPresentedLocked = true;
        lockResource.SendLocked();
        Locked?.Invoke();
    }

    private sealed class Waiter
    {
        private readonly SessionLockManager _owner;
        private readonly Action<OutputStateFields> _onCommitted;
        private readonly Action _onFrame;
        private readonly Action _onDestroyed;
        private bool _committed;

        public Waiter(SessionLockManager owner, IOutput output)
        {
            _owner = owner;
            Output = output;
            _onCommitted = OnCommitted;
            _onFrame = OnFrame;
            _onDestroyed = OnDestroyed;
            output.Committed += _onCommitted;
            output.Frame += _onFrame;
            output.Destroyed += _onDestroyed;
        }

        public IOutput Output { get; }

        public void Detach()
        {
            Output.Committed -= _onCommitted;
            Output.Frame -= _onFrame;
            Output.Destroyed -= _onDestroyed;
        }

        private void OnCommitted(OutputStateFields fields)
        {
            if ((fields & OutputStateFields.Buffer) != 0)
            {
                _committed = true;
            }
        }

        private void OnFrame()
        {
            if (_committed)
            {
                _owner.Complete(this);
            }
        }

        private void OnDestroyed() => _owner.Complete(this);
    }
}
