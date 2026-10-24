using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// A pointer button with modifiers that the compositor reports to the manager in place of the focused surface, while the binding is enabled.
/// </summary>
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

    /// <summary>
    /// Gets a value indicating whether the button is down, between <see cref="Pressed"/> and <see cref="Released"/>.
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
    /// Turns the binding off, so the button goes to the focused surface again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Disable()
    {
        _wm.EnsureManage(nameof(Disable));
        _proxy.Disable();
        IsEnabled = false;
    }
}
