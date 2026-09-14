using Basin.Capabilities;
using Basin.Config;
using static Basin.Hypr.HyprLog;

namespace Basin.Hypr;

public sealed class PreferredTriggerShortcuts : IGlobalShortcuts
{
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _registered = [];
    private readonly Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _bound = [];
    private readonly GlobalShortcutObservers _observers = new();
    private (string AppId, string Id, uint Key)? _held;

    public Func<uint, Modifiers, bool>? IsTaken { get; set; }

    public int Count => _registered.Count;

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

    public bool IsBound(uint keysym, Modifiers modifiers)
    {
        foreach (var chord in _bound.Values)
        {
            if (chord.Keysym == keysym && chord.Modifiers == modifiers)
            {
                return true;
            }
        }

        return false;
    }

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
                Log.Info($"global shortcut {name.AppId}:{name.Id} fired");
                _held = (name.AppId, name.Id, key);
                return true;
            }
        }

        return false;
    }

    private GlobalShortcutInfo Rebind((string AppId, string Id) key, GlobalShortcutInfo shortcut)
    {
        var description = "";
        if (TriggerSyntax.TryParse(shortcut.PreferredTrigger, out var keysym, out var modifiers) &&
            !Collides(key, keysym, modifiers))
        {
            _bound[key] = (keysym, modifiers);
            description = TriggerSyntax.Describe(keysym, modifiers);
            Log.Info($"global shortcut {key.AppId}:{key.Id} bound to {description}, its preferred trigger");
        }
        else
        {
            _bound.Remove(key);
            if (string.IsNullOrEmpty(shortcut.PreferredTrigger))
            {
                Log.Info($"global shortcut {key.AppId}:{key.Id} registered with no preferred trigger and never fires");
            }
            else
            {
                Log.Info($"global shortcut {key.AppId}:{key.Id} asked for {shortcut.PreferredTrigger}, which is taken or unparseable, and stays unbound");
            }
        }

        var stored = shortcut with { TriggerDescription = description };
        _registered[key] = stored;
        return stored;
    }

    private bool Collides((string AppId, string Id) key, uint keysym, Modifiers modifiers)
    {
        if (IsTaken?.Invoke(keysym, modifiers) == true)
        {
            return true;
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
