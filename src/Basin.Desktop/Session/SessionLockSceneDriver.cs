using Basin.Scene;

namespace Basin.Desktop;

/// <summary>
/// Drives a session lock with the scene. It puts each lock surface on its output in a lock tree, clears focus on lock and gives a mapped lock surface keyboard focus.
/// </summary>
public sealed class SessionLockSceneDriver : ISessionLockDriver
{
    private readonly BasinServices? _services;
    private readonly SceneTree _lockTree;
    private readonly OutputLayout _layout;
    private readonly Action<bool> _setLocked;
    private readonly List<(LockSurface Lock, SceneSurface Scene)> _surfaces = [];
    private Seat.Seat? _seat;
    private SessionLockManager? _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLockSceneDriver"/> class that drives <paramref name="manager"/> at once.
    /// </summary>
    /// <param name="manager">The lock manager to drive.</param>
    /// <param name="seat">The seat whose focus the lock clears.</param>
    /// <param name="lockTree">The scene tree that holds the lock surfaces.</param>
    /// <param name="layout">The layout that places the lock surfaces.</param>
    /// <param name="setLocked">The callback that shows or hides normal content. The argument is <see langword="true"/> on lock.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SessionLockSceneDriver(
        SessionLockManager manager,
        Seat.Seat seat,
        SceneTree lockTree,
        OutputLayout layout,
        Action<bool> setLocked)
        : this(lockTree, layout, setLocked)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(seat);
        _seat = seat;
        AttachTo(manager);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLockSceneDriver"/> class and registers it as the <see cref="ISessionLockDriver"/>. The seat comes from the services on first lock.
    /// </summary>
    /// <param name="services">The registry to register in and to find the <see cref="Seat.Seat"/> in.</param>
    /// <param name="lockTree">The scene tree that holds the lock surfaces.</param>
    /// <param name="layout">The layout that places the lock surfaces.</param>
    /// <param name="setLocked">The callback that shows or hides normal content. The argument is <see langword="true"/> on lock.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SessionLockSceneDriver(
        BasinServices services,
        SceneTree lockTree,
        OutputLayout layout,
        Action<bool> setLocked)
        : this(lockTree, layout, setLocked)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        services.Use<ISessionLockDriver>(this);
    }

    private SessionLockSceneDriver(SceneTree lockTree, OutputLayout layout, Action<bool> setLocked)
    {
        ArgumentNullException.ThrowIfNull(lockTree);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(setLocked);
        _lockTree = lockTree;
        _layout = layout;
        _setLocked = setLocked;
    }

    /// <summary>
    /// Gets or sets the text input manager whose focus the lock clears, or <see langword="null"/>.
    /// </summary>
    public TextInputManager? TextInput { get; set; }

    /// <summary>
    /// Occurs when the manager presents the lock, and on <see cref="LockNow"/>.
    /// </summary>
    public event Action? Locked;

    /// <summary>
    /// Occurs after the driver removes the lock surfaces and calls the callback with <see langword="false"/>.
    /// </summary>
    public event Action? Unlocked;

    /// <summary>
    /// Occurs when the locking client dies without an unlock.
    /// </summary>
    public event Action? Abandoned;

    /// <summary>
    /// Occurs when the driver puts a new lock surface in the lock tree.
    /// </summary>
    public event Action<LockSurface, SceneSurface>? LockSurfaceAdded;

    /// <summary>
    /// Locks without a lock client: it hides content, clears focus and raises <see cref="Locked"/>.
    /// </summary>
    public void LockNow()
    {
        OnLockRequested();
        Locked?.Invoke();
    }

    /// <summary>
    /// Unlocks without a lock client: it removes the lock surfaces, shows content and raises <see cref="Unlocked"/>.
    /// </summary>
    public void UnlockNow() => OnUnlocked();

    void ISessionLockDriver.Attach(SessionLockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        AttachTo(manager);
    }

    /// <summary>
    /// Moves each lock surface to its output's box and configures it to the box size. Call it when the layout changes.
    /// </summary>
    public void Reconfigure()
    {
        foreach (var (lockSurface, lockScene) in _surfaces)
        {
            var box = _layout.BoxOf(lockSurface.Output.Output);
            lockScene.Tree.SetPosition(box.X, box.Y);
            lockSurface.Configure(box.Width, box.Height);
        }
    }

    private Seat.Seat LockSeat => _seat ??= _services!.Require<Seat.Seat>();

    private void AttachTo(SessionLockManager manager)
    {
        if (_manager is not null)
        {
            throw new InvalidOperationException("This session-lock driver already drives a lock manager. Use one driver for each lock manager.");
        }

        _manager = manager;
        manager.LockRequested += OnLockRequested;
        manager.Locked += () => Locked?.Invoke();
        manager.Unlocked += OnUnlocked;
        manager.Abandoned += () => Abandoned?.Invoke();
        manager.NewLockSurface += OnNewLockSurface;
    }

    private void OnLockRequested()
    {
        _setLocked(true);
        LockSeat.Keyboard.NotifyClearFocus();
        TextInput?.NotifyFocus(null);
        LockSeat.Pointer.NotifyClearFocus();
    }

    private void OnUnlocked()
    {
        foreach (var (_, lockScene) in _surfaces)
        {
            if (!lockScene.IsDestroyed)
            {
                lockScene.Destroy();
            }
        }

        _surfaces.Clear();
        _setLocked(false);
        Unlocked?.Invoke();
    }

    private void OnNewLockSurface(LockSurface lockSurface)
    {
        var lockScene = new SceneSurface(_lockTree, lockSurface.Surface);
        var box = _layout.BoxOf(lockSurface.Output.Output);
        lockScene.Tree.SetPosition(box.X, box.Y);
        _surfaces.Add((lockSurface, lockScene));
        lockSurface.Mapped += () => LockSeat.Keyboard.NotifyEnter(lockSurface.Surface);
        lockSurface.Unmapped += () =>
        {
            var index = _surfaces.FindIndex(entry => entry.Lock == lockSurface);
            if (index < 0)
            {
                return;
            }

            _surfaces[index].Scene.Destroy();
            _surfaces.RemoveAt(index);
        };
        LockSurfaceAdded?.Invoke(lockSurface, lockScene);
    }
}
