using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_keyboard_shortcuts_inhibit_manager_v1</c> global. The compositor decides whether an inhibitor is active, and only an active inhibitor asks it to pass shortcuts to the client.
/// </summary>
public sealed class KeyboardShortcutsInhibitManager : IDisposable, IZwpKeyboardShortcutsInhibitManagerV1Handler
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorAlreadyInhibited = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat? _seat;
    private readonly Dictionary<Surface, ZwpKeyboardShortcutsInhibitorV1Resource> _inhibitors = [];
    private readonly HashSet<Surface> _active = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardShortcutsInhibitManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="compositor">The compositor that resolves surfaces.</param>
    /// <param name="seat">The seat whose keyboard focus activates inhibitors. When it is <see langword="null"/>, the consumer must call <see cref="Activate"/> and <see cref="Deactivate"/>.</param>
    public KeyboardShortcutsInhibitManager(WlServerDisplay display, CompositorGlobal compositor, Seat.Seat? seat = null)
    {
        _compositor = compositor;
        _seat = seat;
        _global = display.CreateGlobal(ZwpKeyboardShortcutsInhibitManagerV1.Interface, Version, OnBind);
        if (_seat is { } focused)
        {
            focused.Keyboard.FocusChanged += FollowFocus;
        }
    }

    /// <summary>
    /// Occurs when a client creates an inhibitor for a surface, before the manager activates it for a focused surface.
    /// </summary>
    public event Action<Surface>? InhibitorCreated;

    /// <summary>
    /// Stops following keyboard focus and removes the global.
    /// </summary>
    public void Dispose()
    {
        if (_seat is { } focused)
        {
            focused.Keyboard.FocusChanged -= FollowFocus;
        }

        _global.Dispose();
    }

    /// <summary>
    /// Tests whether a client requested an inhibitor for a surface, active or not.
    /// </summary>
    /// <param name="surface">The surface to test, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the surface has an inhibitor.</returns>
    public bool IsInhibited(Surface? surface) =>
        surface is not null && _inhibitors.ContainsKey(surface);

    /// <summary>
    /// Tests whether the inhibitor of a surface is active, so that the compositor must pass shortcuts to the client.
    /// </summary>
    /// <param name="surface">The surface to test, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the surface has an active inhibitor.</returns>
    public bool IsActive(Surface? surface) =>
        surface is not null && _active.Contains(surface);

    /// <summary>
    /// Marks the inhibitor of a surface active and sends <c>active</c> to the client. It does nothing when the surface is already active.
    /// </summary>
    /// <param name="surface">The surface to activate.</param>
    public void Activate(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_active.Add(surface))
        {
            return;
        }

        if (_inhibitors.TryGetValue(surface, out var inhibitor) && !inhibitor.IsDestroyed)
        {
            inhibitor.SendActive();
        }
    }

    /// <summary>
    /// Marks the inhibitor of a surface inactive and sends <c>inactive</c> to the client. It does nothing when the surface is not active.
    /// </summary>
    /// <param name="surface">The surface to deactivate.</param>
    public void Deactivate(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_active.Remove(surface))
        {
            return;
        }

        if (_inhibitors.TryGetValue(surface, out var inhibitor) && !inhibitor.IsDestroyed)
        {
            inhibitor.SendInactive();
        }
    }

    private void FollowFocus(Surface? surface)
    {
        if (_active.Count > 0)
        {
            var live = new Surface[_active.Count];
            _active.CopyTo(live);
            foreach (var held in live)
            {
                if (held != surface)
                {
                    Deactivate(held);
                }
            }
        }

        if (surface is not null && _inhibitors.ContainsKey(surface))
        {
            Activate(surface);
        }
    }

    void IZwpKeyboardShortcutsInhibitManagerV1Handler.Destroy(ZwpKeyboardShortcutsInhibitManagerV1Resource resource)
    {
    }

    void IZwpKeyboardShortcutsInhibitManagerV1Handler.InhibitShortcuts(ZwpKeyboardShortcutsInhibitManagerV1Resource resource, in ZwpKeyboardShortcutsInhibitManagerV1Resource.InhibitShortcutsEventArgs args)
    {
        var inhibitor = new ZwpKeyboardShortcutsInhibitorV1Resource(resource.Client, resource.Version, args.Id);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        if (_inhibitors.ContainsKey(surface))
        {
            resource.PostError(ErrorAlreadyInhibited, "The surface already has a shortcuts inhibitor.");
            return;
        }

        _inhibitors[surface] = inhibitor;
        inhibitor.Destroyed += (_, _) =>
        {
            _inhibitors.Remove(surface);
            _active.Remove(surface);
        };
        surface.Destroyed += () =>
        {
            _inhibitors.Remove(surface);
            _active.Remove(surface);
        };
        InhibitorCreated?.Invoke(surface);
        if (_seat?.Keyboard.Focus == surface)
        {
            Activate(surface);
        }
    }

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new ZwpKeyboardShortcutsInhibitManagerV1Resource(client, version, id, this);
}
