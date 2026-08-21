using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

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

    public bool IsHeld { get; private set; }

    public bool IsEnabled { get; private set; }

    public event Action? Pressed;

    public event Action? Released;

    public event Action? StopRepeat;

    public void Enable()
    {
        _wm.EnsureManage(nameof(Enable));
        _proxy.Enable();
        IsEnabled = true;
    }

    public void Disable()
    {
        _wm.EnsureManage(nameof(Disable));
        _proxy.Disable();
        IsEnabled = false;
    }

    public void SetLayoutOverride(uint layout)
    {
        _wm.EnsureManage(nameof(SetLayoutOverride));
        _proxy.SetLayoutOverride(layout);
    }

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
