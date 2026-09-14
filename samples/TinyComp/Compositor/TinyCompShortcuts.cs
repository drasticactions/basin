using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;

namespace TinyComp;

internal sealed class TinyCompShortcuts : IGlobalShortcuts
{
    private readonly BasinLogger _log;
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _registered = [];
    private readonly Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _bound = [];
    private readonly GlobalShortcutObservers _observers = new();
    private readonly HashSet<(string AppId, string Id)> _reportedRows = [];
    private IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _rows =
        new Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)>();
    private IReadOnlyList<Binding> _bindings = [];
    private (string AppId, string Id, uint Key)? _held;

    public TinyCompShortcuts(BasinLogger log) => _log = log;

    public int Count => _registered.Count;

    public void Configure(Config config)
    {
        _rows = config.Shortcuts;
        _bindings = config.Bindings;
        foreach (var (key, _) in _rows)
        {
            if (!_registered.ContainsKey(key) && _reportedRows.Add(key))
            {
                _log.Debug($"[shortcuts] {key.AppId}:{key.Id} is bound but no client has registered it");
            }
        }

        foreach (var (key, info) in _registered.ToArray())
        {
            Rebind(key, info);
        }
    }

    public bool TryRegister(in GlobalShortcutInfo shortcut)
    {
        var key = (shortcut.AppId, shortcut.Id);
        var replacing = _registered.ContainsKey(key);
        if (replacing && string.IsNullOrEmpty(shortcut.PreferredTrigger))
        {
            return false;
        }

        var stored = Rebind(key, shortcut);
        if (!replacing)
        {
            _observers.Registered(in stored);
        }

        return true;
    }

    public void Unregister(string appId, string id)
    {
        if (_registered.Remove((appId, id), out var removed))
        {
            _bound.Remove((appId, id));
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

    public bool Trigger(string appId, string id, bool pressed, ulong timestampMs)
    {
        if (!_registered.TryGetValue((appId, id), out var info))
        {
            return false;
        }

        if (pressed)
        {
            _observers.Activated(in info, timestampMs);
        }
        else
        {
            _observers.Deactivated(in info, timestampMs);
        }

        return true;
    }

    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    public bool HandleKey(uint key, uint keysym, Modifiers held, bool pressed)
    {
        var now = (ulong)Environment.TickCount64;
        if (!pressed)
        {
            if (_held is not { } active || active.Key != key)
            {
                return false;
            }

            _held = null;
            Trigger(active.AppId, active.Id, pressed: false, now);
            return true;
        }

        if (keysym == Keysym.NoSymbol || _held is not null)
        {
            return false;
        }

        foreach (var (name, chord) in _bound)
        {
            if (chord.Keysym != keysym || chord.Modifiers != held)
            {
                continue;
            }

            if (Trigger(name.AppId, name.Id, pressed: true, now))
            {
                _log.Info($"global shortcut {name.AppId}:{name.Id} fired");
                _held = (name.AppId, name.Id, key);
                return true;
            }
        }

        return false;
    }

    private GlobalShortcutInfo Rebind((string AppId, string Id) key, GlobalShortcutInfo shortcut)
    {
        var description = "";
        if (_rows.TryGetValue(key, out var row))
        {
            _bound[key] = row;
            description = TriggerSyntax.Describe(row.Keysym, row.Modifiers);
            _log.Info($"global shortcut {key.AppId}:{key.Id} bound to {description} from [shortcuts]");
        }
        else if (TriggerSyntax.TryParse(shortcut.PreferredTrigger, out var keysym, out var modifiers) &&
            !Collides(key, keysym, modifiers))
        {
            _bound[key] = (keysym, modifiers);
            description = TriggerSyntax.Describe(keysym, modifiers);
            _log.Info($"global shortcut {key.AppId}:{key.Id} bound to {description}, its preferred trigger");
        }
        else
        {
            _bound.Remove(key);
            if (string.IsNullOrEmpty(shortcut.PreferredTrigger))
            {
                _log.Info($"global shortcut {key.AppId}:{key.Id} registered with no [shortcuts] row and never fires");
            }
            else
            {
                _log.Info($"global shortcut {key.AppId}:{key.Id} asked for {shortcut.PreferredTrigger}, which is taken or unparseable, and stays unbound");
            }
        }

        var stored = shortcut with { TriggerDescription = description };
        _registered[key] = stored;
        return stored;
    }

    private bool Collides((string AppId, string Id) key, uint keysym, Modifiers modifiers)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Keysym == keysym && binding.ModifierMask == modifiers)
            {
                return true;
            }
        }

        foreach (var (other, chord) in _bound)
        {
            if (other != key && chord.Keysym == keysym && chord.Modifiers == modifiers)
            {
                return true;
            }
        }

        return false;
    }
}
