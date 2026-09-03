using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Hypr;

namespace TinyComp;

internal sealed class HyprShortcuts : IGlobalShortcuts
{
    private readonly BasinLogger _log;
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _registered = [];
    private readonly GlobalShortcutObservers _observers = new();
    private readonly HashSet<(string AppId, string Id)> _reportedRows = [];
    private IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _rows =
        new Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)>();
    private (string AppId, string Id, uint Key)? _held;

    public HyprShortcuts(BasinLogger log) => _log = log;

    public int Count => _registered.Count;

    public void Configure(Config config)
    {
        _rows = config.HyprShortcuts;
        foreach (var (key, _) in _rows)
        {
            if (!_registered.ContainsKey(key) && _reportedRows.Add(key))
            {
                _log.Debug($"[hypr.shortcuts] {key.AppId}:{key.Id} is bound but no client has registered it");
            }
        }

        foreach (var (key, _) in _registered)
        {
            if (!_rows.ContainsKey(key))
            {
                _log.Info($"hypr shortcut {key.AppId}:{key.Id} has no [hypr.shortcuts] row and never fires");
            }
        }
    }

    public bool TryRegister(in GlobalShortcutInfo shortcut)
    {
        var key = (shortcut.AppId, shortcut.Id);
        if (!_registered.TryAdd(key, shortcut))
        {
            return false;
        }

        if (_rows.ContainsKey(key))
        {
            _log.Info($"hypr shortcut {shortcut.AppId}:{shortcut.Id} registered: {shortcut.Description}");
        }
        else
        {
            _log.Info($"hypr shortcut {shortcut.AppId}:{shortcut.Id} registered with no [hypr.shortcuts] row and never fires");
        }

        _observers.Registered(in shortcut);
        return true;
    }

    public void Unregister(string appId, string id)
    {
        if (_registered.Remove((appId, id), out var removed))
        {
            if (_held is { } held && held.AppId == appId && held.Id == id)
            {
                _held = null;
            }

            _observers.Removed(in removed);
        }
    }

    public int Enumerate(Span<GlobalShortcutInfo> shortcuts)
    {
        if (shortcuts.Length < _registered.Count)
        {
            return -1;
        }

        var i = 0;
        foreach (var info in _registered.Values)
        {
            shortcuts[i++] = info;
        }

        return i;
    }

    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    public bool HandleKey(uint key, uint keysym, Modifiers held, bool pressed, HyprlandGlobalShortcutsManager? manager)
    {
        if (manager is null)
        {
            return false;
        }

        if (!pressed)
        {
            if (_held is not { } active || active.Key != key)
            {
                return false;
            }

            _held = null;
            manager.Trigger(active.AppId, active.Id, pressed: false);
            return true;
        }

        if (keysym == Keysym.NoSymbol || _held is not null)
        {
            return false;
        }

        foreach (var (name, chord) in _rows)
        {
            if (chord.Keysym != keysym || chord.Modifiers != held || !_registered.ContainsKey(name))
            {
                continue;
            }

            if (manager.Trigger(name.AppId, name.Id, pressed: true))
            {
                _log.Info($"hypr shortcut {name.AppId}:{name.Id} fired");
                _held = (name.AppId, name.Id, key);
                return true;
            }
        }

        return false;
    }
}
