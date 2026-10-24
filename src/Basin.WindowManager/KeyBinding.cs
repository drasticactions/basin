using Basin.Config;
using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// A keysym with modifiers that the compositor reports to the manager in place of the focused surface, while the binding is enabled.
/// </summary>
public sealed class KeyBinding
{
    private readonly RiverWindowManager _wm;
    private readonly WmBindings _owner;
    private readonly RiverXkbBindingV1 _proxy;
    private readonly uint _keysym;
    private readonly Modifiers _modifiers;
    private Notifications _notifications;

    internal KeyBinding(RiverWindowManager wm, WmBindings owner, RiverXkbBindingV1 proxy, uint keysym, Modifiers modifiers)
    {
        _wm = wm;
        _owner = owner;
        _proxy = proxy;
        _keysym = keysym;
        _modifiers = modifiers;

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
    /// Gets a value indicating whether the binding object is destroyed. A destroyed binding is not enabled and cannot be enabled again.
    /// </summary>
    public bool IsDestroyed => _proxy.IsDestroyed;

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
    /// <exception cref="ObjectDisposedException">The binding is destroyed.</exception>
    public void Enable()
    {
        ThrowIfDestroyed(nameof(Enable));
        _wm.EnsureManage(nameof(Enable));
        _proxy.Enable();
        IsEnabled = true;
    }

    /// <summary>
    /// Turns the binding off, so the key goes to the focused surface again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="ObjectDisposedException">The binding is destroyed.</exception>
    public void Disable()
    {
        ThrowIfDestroyed(nameof(Disable));
        _wm.EnsureManage(nameof(Disable));
        _proxy.Disable();
        IsEnabled = false;
    }

    /// <summary>
    /// Matches the binding against one xkb layout, whatever layout is active.
    /// </summary>
    /// <param name="layout">The zero-based layout index of the keyboard that sends the key.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="ObjectDisposedException">The binding is destroyed.</exception>
    public void SetLayoutOverride(uint layout)
    {
        ThrowIfDestroyed(nameof(SetLayoutOverride));
        _wm.EnsureManage(nameof(SetLayoutOverride));
        _proxy.SetLayoutOverride(layout);
    }

    /// <summary>
    /// Destroys the binding object and removes it from the bindings of its <see cref="WmBindings"/>, so a later submap does not touch it. A second call does nothing.
    /// </summary>
    public void Destroy()
    {
        WmThreadAffinity.Assert();
        if (_proxy.IsDestroyed)
        {
            return;
        }

        _owner.Forget(this);
        IsEnabled = false;
        DestroyProxy();
    }

    internal void DestroyProxy()
    {
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }

    private void ThrowIfDestroyed(string request)
    {
        if (_proxy.IsDestroyed)
        {
            throw new ObjectDisposedException(
                nameof(KeyBinding),
                $"'{request}' was called on a destroyed key binding (keysym 0x{_keysym:x}, modifiers {_modifiers}). Make a new binding.");
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
