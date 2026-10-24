using Basin.Capabilities;
using Basin.Config;
using static Basin.Hypr.HyprLog;

namespace Basin.Hypr;

/// <summary>
/// An <see cref="IGlobalShortcuts"/> registry that binds each shortcut to a key chord and fires it from <see cref="HandleKey"/>. The chord comes from <see cref="Overrides"/>, or else from the preferred trigger of the shortcut.
/// </summary>
public sealed class PreferredTriggerShortcuts : IGlobalShortcuts
{
    private readonly Dictionary<(string AppId, string Id), GlobalShortcutInfo> _registered = [];
    private readonly Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _bound = [];
    private readonly GlobalShortcutObservers _observers = new();
    private (string AppId, string Id, uint Key, GlobalShortcutFlags Flags)? _held;
    private IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> _overrides =
        new Dictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)>();

    /// <summary>
    /// Gets or sets a check that reports a chord that the compositor already uses. A preferred trigger that it reports as taken stays unbound. Overrides skip the check.
    /// </summary>
    public Func<uint, Modifiers, bool>? IsTaken { get; set; }

    /// <summary>
    /// Gets or sets the chords that the compositor assigns by application id and shortcut id. They win over the preferred triggers. Setting the value rebinds every shortcut.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<(string AppId, string Id), (uint Keysym, Modifiers Modifiers)> Overrides
    {
        get => _overrides;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _overrides = value;
            Rebind();
        }
    }

    /// <summary>
    /// Tells whether a shortcut is in the registry, bound to a chord or not.
    /// </summary>
    /// <param name="appId">The application that owns the shortcut.</param>
    /// <param name="id">The identifier of the shortcut.</param>
    /// <returns><see langword="true"/> when the shortcut is registered.</returns>
    public bool IsRegistered(string appId, string id) => _registered.ContainsKey((appId, id));

    /// <summary>
    /// Computes the chord of every registered shortcut again, for example after <see cref="IsTaken"/> starts to report a different set. It does not notify the observers.
    /// </summary>
    public void Rebind()
    {
        foreach (var (key, info) in _registered.ToArray())
        {
            Rebind(key, info);
        }
    }

    /// <summary>
    /// Gets the number of registered shortcuts, bound or not.
    /// </summary>
    public int Count => _registered.Count;

    /// <summary>
    /// Adds a shortcut and binds its chord. A second registration of the same pair with a preferred trigger replaces the first one and does not notify the observers.
    /// </summary>
    /// <param name="shortcut">The shortcut to add. The stored copy gets a trigger description of the chord, or an empty one when it stays unbound.</param>
    /// <returns><see langword="false"/> when the pair is already registered and <paramref name="shortcut"/> has no preferred trigger.</returns>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void AddObserver(IGlobalShortcutObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IGlobalShortcutObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Tells whether a registered shortcut is bound to an exact chord.
    /// </summary>
    /// <param name="keysym">The keysym of the chord.</param>
    /// <param name="modifiers">The modifiers of the chord. They must match exactly.</param>
    /// <returns><see langword="true"/> when a shortcut uses the chord.</returns>
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

    /// <summary>
    /// Fires a bound shortcut on a key press and releases it on the release of the same key. Only one shortcut can be held at a time. A one-shot shortcut is unregistered on its release.
    /// </summary>
    /// <param name="key">The evdev key code, used to match the release to the press.</param>
    /// <param name="keysym">The keysym of the pressed key.</param>
    /// <param name="held">The modifiers that are held. They must match the chord exactly.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the compositor must not send the key to the focused client. A <see cref="GlobalShortcutFlags.NoBlock"/> shortcut returns <see langword="false"/>.</returns>
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
            if ((active.Flags & GlobalShortcutFlags.OneShot) != 0)
            {
                Unregister(active.AppId, active.Id);
            }

            return (active.Flags & GlobalShortcutFlags.NoBlock) == 0;
        }

        if (keysym == Keysym.NoSymbol || _held is not null)
        {
            return false;
        }

        foreach (var (name, chord) in _bound)
        {
            if (chord.Keysym != keysym || chord.Modifiers != held || !_registered.TryGetValue(name, out var info))
            {
                continue;
            }

            if (Trigger(name.AppId, name.Id, pressed: true, now))
            {
                Log.Debug($"global shortcut {name.AppId}:{name.Id} fired");
                _held = (name.AppId, name.Id, key, info.Flags);
                return (info.Flags & GlobalShortcutFlags.NoBlock) == 0;
            }
        }

        return false;
    }

    private GlobalShortcutInfo Rebind((string AppId, string Id) key, GlobalShortcutInfo shortcut)
    {
        var description = "";
        if (_overrides.TryGetValue(key, out var fixedChord))
        {
            _bound[key] = fixedChord;
            description = TriggerSyntax.Describe(fixedChord.Keysym, fixedChord.Modifiers);
            Log.Info($"global shortcut {key.AppId}:{key.Id} bound to {description} by the compositor");
        }
        else if (TriggerSyntax.TryParse(shortcut.PreferredTrigger, out var keysym, out var modifiers) &&
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
                Log.Info($"global shortcut {key.AppId}:{key.Id} has no preferred trigger and no compositor binding, it stays unbound");
            }
            else
            {
                Log.Info($"global shortcut {key.AppId}:{key.Id} trigger {shortcut.PreferredTrigger} is taken or not valid, it stays unbound");
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
