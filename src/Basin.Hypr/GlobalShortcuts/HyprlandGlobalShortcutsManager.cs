using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>hyprland_global_shortcuts_manager_v1</c>. It registers each client shortcut in an <see cref="IGlobalShortcuts"/> registry and forwards presses and releases to the client.
/// </summary>
public sealed class HyprlandGlobalShortcutsManager : IGlobalShortcutObserver, IDisposable
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IGlobalShortcuts _registry;
    private readonly Dictionary<(string AppId, string Id), HyprlandGlobalShortcutV1Resource> _shortcuts = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandGlobalShortcutsManager"/> class and subscribes to <paramref name="registry"/>.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="registry">The registry that holds the shortcuts and decides their triggers.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HyprlandGlobalShortcutsManager(WlServerDisplay display, IGlobalShortcuts registry)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _registry.AddObserver(this);
        _global = display.CreateGlobal(HyprlandGlobalShortcutsManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets the registry that the client shortcuts go into.
    /// </summary>
    public IGlobalShortcuts Registry => _registry;

    /// <summary>
    /// Gets the number of shortcuts that clients registered through this global and did not destroy.
    /// </summary>
    public int Count => _shortcuts.Count;

    /// <summary>
    /// Tells whether a client registered a shortcut through this global. A shortcut that another source put in the registry does not count.
    /// </summary>
    /// <param name="appId">The application id that the client gave.</param>
    /// <param name="id">The shortcut id that the client gave.</param>
    /// <returns><see langword="true"/> when a live <c>hyprland_global_shortcut_v1</c> holds the pair.</returns>
    public bool IsRegistered(string appId, string id) => _shortcuts.ContainsKey((appId, id));

    /// <summary>
    /// Does nothing. The manager tracks only the shortcuts that its clients registered.
    /// </summary>
    /// <param name="shortcut">The shortcut that was added. It is ignored.</param>
    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    /// <summary>
    /// Does nothing. The shortcut object of the client stays alive and stops receiving events.
    /// </summary>
    /// <param name="shortcut">The shortcut that was removed. It is ignored.</param>
    public void ShortcutRemoved(in GlobalShortcutInfo shortcut)
    {
    }

    /// <summary>
    /// Sends <c>pressed</c> to the client that owns the shortcut, with the current monotonic time.
    /// </summary>
    /// <param name="shortcut">The shortcut that was pressed. A shortcut that no client of this global owns is ignored.</param>
    /// <param name="timestampMs">The time of the press. The event uses the current clock instead.</param>
    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Send(in shortcut, pressed: true);

    /// <summary>
    /// Sends <c>released</c> to the client that owns the shortcut, with the current monotonic time.
    /// </summary>
    /// <param name="shortcut">The shortcut that was released. A shortcut that no client of this global owns is ignored.</param>
    /// <param name="timestampMs">The time of the release. The event uses the current clock instead.</param>
    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Send(in shortcut, pressed: false);

    /// <summary>
    /// Unsubscribes from the registry and destroys the global.
    /// </summary>
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
