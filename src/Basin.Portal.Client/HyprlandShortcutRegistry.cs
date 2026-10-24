using Basin.Capabilities;
using Basin.Portal.Client.Protocol;

namespace Basin.Portal.Client;

/// <summary>
/// Implements <see cref="IGlobalShortcuts"/> over <c>hyprland_global_shortcuts_v1</c> for a portal client.
/// The compositor binds the keys and reports presses, so <see cref="Trigger"/> does nothing here.
/// </summary>
public sealed class HyprlandShortcutRegistry : IGlobalShortcuts, IDisposable
{
    private readonly HyprlandGlobalShortcutsManagerV1 _manager;
    private readonly Dictionary<(string AppId, string Id), Entry> _entries = [];
    private readonly GlobalShortcutObservers _observers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandShortcutRegistry"/> class.
    /// </summary>
    /// <param name="manager">The bound global shortcuts manager.</param>
    public HyprlandShortcutRegistry(HyprlandGlobalShortcutsManagerV1 manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// Registers a shortcut with the compositor and notifies the observers. The preferred trigger becomes the trigger description.
    /// </summary>
    /// <param name="shortcut">The shortcut to register.</param>
    /// <returns><see langword="false"/> when a shortcut with the same app id and id is registered.</returns>
    public bool TryRegister(in GlobalShortcutInfo shortcut)
    {
        var key = (shortcut.AppId, shortcut.Id);
        if (_entries.ContainsKey(key))
        {
            return false;
        }

        var wire = _manager.RegisterShortcut(shortcut.Id, shortcut.AppId, shortcut.Description, shortcut.PreferredTrigger);
        var info = shortcut with { TriggerDescription = shortcut.PreferredTrigger };
        var entry = new Entry(wire, info);
        _entries[key] = entry;
        wire.Pressed += (_, e) => Fire(key, true, Millis(e.TvSecHi, e.TvSecLo, e.TvNsec));
        wire.Released += (_, e) => Fire(key, false, Millis(e.TvSecHi, e.TvSecLo, e.TvNsec));
        _observers.Registered(in info);
        return true;
    }

    /// <inheritdoc/>
    public void Unregister(string appId, string id)
    {
        var key = (appId, id);
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        if (!entry.Wire.IsDestroyed)
        {
            entry.Wire.Destroy();
        }

        _observers.Removed(entry.Info);
    }

    /// <summary>
    /// Copies all registered shortcuts into a span.
    /// </summary>
    /// <param name="shortcuts">The span that receives the shortcuts.</param>
    /// <returns>The number of shortcuts, or -1 when the span is too short.</returns>
    public int Enumerate(Span<GlobalShortcutInfo> shortcuts)
    {
        if (shortcuts.Length < _entries.Count)
        {
            return -1;
        }

        var i = 0;
        foreach (var entry in _entries.Values)
        {
            shortcuts[i++] = entry.Info;
        }

        return i;
    }

    /// <summary>
    /// Refuses every trigger, because the compositor reports the presses itself.
    /// </summary>
    /// <param name="appId">The app id. It is ignored.</param>
    /// <param name="id">The shortcut id. It is ignored.</param>
    /// <param name="pressed">The press state. It is ignored.</param>
    /// <param name="timestampMs">The timestamp. It is ignored.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool Trigger(string appId, string id, bool pressed, ulong timestampMs) => false;

    /// <inheritdoc/>
    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Destroys every registered shortcut in the compositor. It does not notify the observers.
    /// </summary>
    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            if (!entry.Wire.IsDestroyed)
            {
                entry.Wire.Destroy();
            }
        }

        _entries.Clear();
    }

    private void Fire((string AppId, string Id) key, bool pressed, ulong timestampMs)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return;
        }

        if (pressed)
        {
            _observers.Activated(entry.Info, timestampMs);
        }
        else
        {
            _observers.Deactivated(entry.Info, timestampMs);
        }
    }

    private static ulong Millis(uint secHi, uint secLo, uint nsec) =>
        ((((ulong)secHi << 32) | secLo) * 1000) + (nsec / 1_000_000);

    private sealed class Entry(HyprlandGlobalShortcutV1 wire, GlobalShortcutInfo info)
    {
        public HyprlandGlobalShortcutV1 Wire { get; } = wire;

        public GlobalShortcutInfo Info { get; } = info;
    }
}
