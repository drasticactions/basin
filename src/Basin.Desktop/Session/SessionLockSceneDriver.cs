using Basin.Scene;

namespace Basin.Desktop;

public sealed class SessionLockSceneDriver
{
    private readonly Seat.Seat _seat;
    private readonly SceneTree _lockTree;
    private readonly OutputLayout _layout;
    private readonly Action<bool> _setLocked;
    private readonly List<(LockSurface Lock, SceneSurface Scene)> _surfaces = [];

    public SessionLockSceneDriver(
        SessionLockManager manager,
        Seat.Seat seat,
        SceneTree lockTree,
        OutputLayout layout,
        Action<bool> setLocked)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(lockTree);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(setLocked);
        _seat = seat;
        _lockTree = lockTree;
        _layout = layout;
        _setLocked = setLocked;

        manager.LockRequested += OnLockRequested;
        manager.Locked += () => Locked?.Invoke();
        manager.Unlocked += OnUnlocked;
        manager.Abandoned += () => Abandoned?.Invoke();
        manager.NewLockSurface += OnNewLockSurface;
    }

    public TextInputManager? TextInput { get; set; }

    public event Action? Locked;

    public event Action? Unlocked;

    public event Action? Abandoned;

    public event Action<LockSurface, SceneSurface>? LockSurfaceAdded;

    public void LockNow()
    {
        OnLockRequested();
        Locked?.Invoke();
    }

    public void UnlockNow() => OnUnlocked();

    public void Reconfigure()
    {
        foreach (var (lockSurface, lockScene) in _surfaces)
        {
            var box = _layout.BoxOf(lockSurface.Output.Output);
            lockScene.Tree.SetPosition(box.X, box.Y);
            lockSurface.Configure(box.Width, box.Height);
        }
    }

    private void OnLockRequested()
    {
        _setLocked(true);
        _seat.Keyboard.NotifyClearFocus();
        TextInput?.NotifyFocus(null);
        _seat.Pointer.NotifyClearFocus();
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
        lockSurface.Mapped += () => _seat.Keyboard.NotifyEnter(lockSurface.Surface);
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
