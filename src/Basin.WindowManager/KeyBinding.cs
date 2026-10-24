using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// A keysym with modifiers that the compositor reports to the manager in place of the focused surface, while the binding is enabled.
/// </summary>
public sealed class KeyBinding
{
    private readonly RiverWindowManager _wm;
    private readonly RiverXkbBindingV1 _proxy;
    private Notifications _notifications;

    internal KeyBinding(RiverWindowManager wm, RiverXkbBindingV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;

        proxy.Pressed += (_, _) =>
        {
            IsHeld = true;
            _notifications |= Notifications.Pressed;
            Fire();
        };
        proxy.Released += (_, _) =>
        {
            IsHeld = false;
            _notifications |= Notifications.Released;
            Fire();
        };
        proxy.StopRepeat += (_, _) =>
        {
            _notifications |= Notifications.StopRepeat;
            Fire();
        };
    }

    /// <summary>
    /// Gets a value indicating whether the key is down, between <see cref="Pressed"/> and <see cref="Released"/>.
    /// </summary>
    public bool IsHeld { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last call was <see cref="Enable"/> and not <see cref="Disable"/>.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Occurs when the press event arrives from the compositor.
    /// </summary>
    public event Action? Pressed;

    /// <summary>
    /// Occurs when the release event arrives from the compositor.
    /// </summary>
    public event Action? Released;

    /// <summary>
    /// Occurs when another key went down while this one is held, so a manager that repeats the action stops.
    /// </summary>
    public event Action? StopRepeat;

    /// <summary>
    /// Turns the binding on.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Enable()
    {
        _wm.EnsureManage(nameof(Enable));
        _proxy.Enable();
        IsEnabled = true;
    }

    /// <summary>
    /// Turns the binding off, so the key goes to the focused surface again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Disable()
    {
        _wm.EnsureManage(nameof(Disable));
        _proxy.Disable();
        IsEnabled = false;
    }

    /// <summary>
    /// Matches the binding against one xkb layout, whatever layout is active.
    /// </summary>
    /// <param name="layout">The zero-based layout index of the keyboard that sends the key.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void SetLayoutOverride(uint layout)
    {
        _wm.EnsureManage(nameof(SetLayoutOverride));
        _proxy.SetLayoutOverride(layout);
    }

    /// <summary>
    /// Destroys the binding object. A second call does nothing.
    /// </summary>
    public void Destroy()
    {
        WmThreadAffinity.Assert();
        DestroyProxy();
    }

    internal void DestroyProxy()
    {
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }

    private void Fire()
    {
        var pending = _notifications;
        _notifications = Notifications.None;

        if ((pending & Notifications.Pressed) != 0)
        {
            Pressed?.Invoke();
        }

        if ((pending & Notifications.StopRepeat) != 0)
        {
            StopRepeat?.Invoke();
        }

        if ((pending & Notifications.Released) != 0)
        {
            Released?.Invoke();
        }
    }

    [Flags]
    private enum Notifications
    {
        None = 0,
        Pressed = 1,
        Released = 2,
        StopRepeat = 4,
    }
}
