using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_session_lock_manager_v1</c> global. It allows one lock at a time, and sends <c>locked</c> after each enabled output commits a frame.
/// </summary>
public sealed class SessionLockManager : IDisposable, IExtSessionLockManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly WlServerDisplay _display;
    private readonly OutputLayout? _layout;
    private readonly List<Waiter> _waiters = [];
    private ExtSessionLockV1Resource? _activeLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLockManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global and supplies serials.</param>
    /// <param name="compositor">The compositor global that resolves lock surfaces.</param>
    /// <param name="layout">The layout whose enabled outputs must repaint before the lock is presented, or <see langword="null"/> to present at once.</param>
    public SessionLockManager(WlServerDisplay display, CompositorGlobal compositor, OutputLayout? layout = null)
    {
        _display = display;
        _compositor = compositor;
        _layout = layout;
        _global = display.CreateGlobal(ExtSessionLockManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets a value indicating whether a client asked for a lock and has not unlocked. It stays <see langword="true"/> when the locking client dies.
    /// </summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the lock is on screen and the client got <c>locked</c>.
    /// </summary>
    public bool IsPresentedLocked { get; private set; }

    /// <summary>
    /// Occurs when a client asks for a lock while the session is not locked. The consumer must hide all content and clear focus at once.
    /// </summary>
    public event Action? LockRequested;

    /// <summary>
    /// Occurs when the lock is presented on every enabled output.
    /// </summary>
    public event Action? Locked;

    /// <summary>
    /// Occurs when the locking client unlocks.
    /// </summary>
    public event Action? Unlocked;

    /// <summary>
    /// Occurs when the locking client destroys the lock without an unlock. The session stays locked.
    /// </summary>
    public event Action? Abandoned;

    /// <summary>
    /// Occurs when the locking client creates a lock surface.
    /// </summary>
    public event Action<LockSurface>? NewLockSurface;

    /// <summary>
    /// Stops waiting for outputs and removes the global.
    /// </summary>
    public void Dispose()
    {
        CancelWaiters();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ExtSessionLockManagerV1Resource(client, version, id, this);

    void IExtSessionLockManagerV1Handler.Destroy(ExtSessionLockManagerV1Resource resource)
    {
    }

    void IExtSessionLockManagerV1Handler.Lock(ExtSessionLockManagerV1Resource resource, in ExtSessionLockManagerV1Resource.LockEventArgs args)
    {
        var sessionLock = new SessionLock(this, resource.Client, resource.Version, args.Id);
        var lockResource = sessionLock.Resource;
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

        sessionLock.Activate();
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

    private sealed class SessionLock : IExtSessionLockV1Handler
    {
        private readonly SessionLockManager _owner;
        private bool _active;
        private bool _unlocked;

        public SessionLock(SessionLockManager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            Resource = new ExtSessionLockV1Resource(client, version, id, this);
        }

        public ExtSessionLockV1Resource Resource { get; }

        public void Activate()
        {
            _active = true;
            Resource.Destroyed += OnDestroyed;
        }

        void IExtSessionLockV1Handler.Destroy(ExtSessionLockV1Resource resource)
        {
        }

        void IExtSessionLockV1Handler.UnlockAndDestroy(ExtSessionLockV1Resource resource)
        {
            if (!_active)
            {
                return;
            }

            _unlocked = true;
            _owner.CancelWaiters();
            _owner.IsLocked = false;
            _owner._activeLock = null;
            _owner.IsPresentedLocked = false;
            _owner.Unlocked?.Invoke();
        }

        void IExtSessionLockV1Handler.GetLockSurface(ExtSessionLockV1Resource resource, in ExtSessionLockV1Resource.GetLockSurfaceEventArgs args)
        {
            if (!_active)
            {
                return;
            }

            var surface = _owner._compositor.ResolveSurface(args.Surface);
            var output = OutputGlobal.FromResource(args.Output);
            if (surface is null || output is null)
            {
                _ = new ExtSessionLockSurfaceV1Resource(resource.Client, resource.Version, args.Id);
                return;
            }

            if (!surface.CanSetRole(LockSurface.RoleName))
            {
                _ = new ExtSessionLockSurfaceV1Resource(resource.Client, resource.Version, args.Id);
                resource.PostError((uint)ExtSessionLockV1.Error.Role, "The surface already has a role.");
                return;
            }

            var lockSurface = new LockSurface(_owner._display, surface, resource.Client, resource.Version, args.Id, output);
            surface.TrySetRole(LockSurface.RoleName, lockSurface);
            _owner.NewLockSurface?.Invoke(lockSurface);
        }

        private void OnDestroyed(object? sender, EventArgs e)
        {
            if (!_unlocked && ReferenceEquals(_owner._activeLock, Resource))
            {
                _owner.CancelWaiters();
                _owner._activeLock = null;
                _owner.Abandoned?.Invoke();
            }
        }
    }
}
