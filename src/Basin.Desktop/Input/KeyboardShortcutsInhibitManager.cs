using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

public sealed class KeyboardShortcutsInhibitManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorAlreadyInhibited = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Seat.Seat? _seat;
    private readonly Dictionary<Surface, ZwpKeyboardShortcutsInhibitorV1Resource> _inhibitors = [];
    private readonly HashSet<Surface> _active = [];

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

    public event Action<Surface>? InhibitorCreated;

    public void Dispose()
    {
        if (_seat is { } focused)
        {
            focused.Keyboard.FocusChanged -= FollowFocus;
        }

        _global.Dispose();
    }

    public bool IsInhibited(Surface? surface) =>
        surface is not null && _inhibitors.ContainsKey(surface);

    public bool IsActive(Surface? surface) =>
        surface is not null && _active.Contains(surface);

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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpKeyboardShortcutsInhibitManagerV1Resource(client, version, id);
        manager.InhibitShortcuts += (_, e) =>
        {
            var inhibitor = new ZwpKeyboardShortcutsInhibitorV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (_inhibitors.ContainsKey(surface))
            {
                manager.PostError(ErrorAlreadyInhibited, "surface already has a shortcuts inhibitor");
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
        };
    }
}
