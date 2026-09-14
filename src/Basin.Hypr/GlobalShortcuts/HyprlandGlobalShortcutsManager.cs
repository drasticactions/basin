using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandGlobalShortcutsManager : IGlobalShortcutObserver, IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IGlobalShortcuts _registry;
    private readonly Dictionary<(string AppId, string Id), HyprlandGlobalShortcutV1Resource> _shortcuts = [];

    public HyprlandGlobalShortcutsManager(WlServerDisplay display, IGlobalShortcuts registry)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _registry.AddObserver(this);
        _global = display.CreateGlobal(HyprlandGlobalShortcutsManagerV1.Interface, Version, OnBind);
    }

    public IGlobalShortcuts Registry => _registry;

    public int Count => _shortcuts.Count;

    public bool IsRegistered(string appId, string id) => _shortcuts.ContainsKey((appId, id));

    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutRemoved(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Send(in shortcut, pressed: true);

    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Send(in shortcut, pressed: false);

    public void Dispose()
    {
        _registry.RemoveObserver(this);
        _global.Dispose();
    }

    private void Send(in GlobalShortcutInfo info, bool pressed)
    {
        if (!_shortcuts.TryGetValue((info.AppId, info.Id), out var shortcut) || shortcut.IsDestroyed)
        {
            return;
        }

        var nanos = MonotonicClock.Nanos;
        var seconds = (ulong)(nanos / 1_000_000_000);
        var hi = (uint)(seconds >> 32);
        var lo = (uint)seconds;
        var nsec = (uint)(nanos % 1_000_000_000);
        if (pressed)
        {
            shortcut.SendPressed(hi, lo, nsec);
        }
        else
        {
            shortcut.SendReleased(hi, lo, nsec);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandGlobalShortcutsManagerV1Resource(client, version, id);
        manager.RegisterShortcut += (_, e) =>
        {
            var resource = new HyprlandGlobalShortcutV1Resource(client, manager.Version, e.Shortcut);
            var key = (e.AppId, e.Id);
            var info = new GlobalShortcutInfo(e.AppId, e.Id, e.Description, e.TriggerDescription, e.TriggerDescription);
            if (_shortcuts.ContainsKey(key) || !_registry.TryRegister(in info))
            {
                manager.PostError(
                    (uint)HyprlandGlobalShortcutsManagerV1.Error.AlreadyTaken,
                    "the app_id and id combination is already registered");
                return;
            }

            _shortcuts[key] = resource;
            resource.Destroyed += (_, _) =>
            {
                if (_shortcuts.Remove(key))
                {
                    _registry.Unregister(e.AppId, e.Id);
                }
            };
        };
    }
}
