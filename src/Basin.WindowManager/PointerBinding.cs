using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class PointerBinding
{
    private readonly RiverWindowManager _wm;
    private readonly RiverPointerBindingV1 _proxy;

    internal PointerBinding(RiverWindowManager wm, RiverPointerBindingV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;
        proxy.Pressed += (_, _) =>
        {
            IsHeld = true;
            Pressed?.Invoke();
        };
        proxy.Released += (_, _) =>
        {
            IsHeld = false;
            Released?.Invoke();
        };
    }

    public bool IsHeld { get; private set; }

    public bool IsEnabled { get; private set; }

    public event Action? Pressed;

    public event Action? Released;

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
}
