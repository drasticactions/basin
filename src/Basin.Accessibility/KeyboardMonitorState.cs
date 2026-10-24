using Basin.Capabilities;

namespace Basin.Accessibility;

internal sealed class KeyboardMonitorState
{
    private readonly Dictionary<string, KeyboardMonitorClient> _clients = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _grabbedModifiers = [];
    private readonly Dictionary<uint, uint> _lastModifierPress = [];
    private readonly HashSet<uint> _suppressed = [];

    public int ClientCount => _clients.Count;

    public KeyboardMonitorClient Client(string name)
    {
        if (!_clients.TryGetValue(name, out var client))
        {
            client = new KeyboardMonitorClient();
            _clients[name] = client;
        }

        return client;
    }

    public void SetKeyGrabs(string name, IEnumerable<uint> modifiers, IEnumerable<(uint Keysym, uint Modifiers)> keystrokes)
    {
        var client = Client(name);
        client.Modifiers = [.. modifiers];
        client.Keystrokes = [.. keystrokes];
        RebuildGrabbedModifiers();
    }

    public void Forget(string name)
    {
        if (_clients.Remove(name))
        {
            RebuildGrabbedModifiers();
        }
    }

    public void Clear()
    {
        _clients.Clear();
        _grabbedModifiers.Clear();
        _lastModifierPress.Clear();
        _suppressed.Clear();
    }

    public KeyMonitorVerdict Process(in KeyMonitorEvent key, List<string> watchers)
    {
        watchers.Clear();
        foreach (var (name, client) in _clients)
        {
            if (client.ShouldWatch(_suppressed, key.Modifiers, key.Keysym))
            {
                watchers.Add(name);
            }
        }

        if (_grabbedModifiers.Contains(key.Keysym))
        {
            if (key.Released)
            {
                if (!_suppressed.Contains(key.Keysym))
                {
                    return KeyMonitorVerdict.Pass;
                }
            }
            else
            {
                var hadLast = _lastModifierPress.TryGetValue(key.Keysym, out var last);
                _lastModifierPress[key.Keysym] = key.TimeMs;
                if (hadLast && unchecked(key.TimeMs - last) <= key.RepeatDelayMs)
                {
                    return KeyMonitorVerdict.Pass;
                }
            }
        }

        if (key.Released)
        {
            return _suppressed.Remove(key.Keysym) ? KeyMonitorVerdict.Block : KeyMonitorVerdict.Pass;
        }

        if (_suppressed.Contains(key.Keysym))
        {
            return KeyMonitorVerdict.Block;
        }

        foreach (var client in _clients.Values)
        {
            if (client.ShouldGrab(_suppressed, key.Modifiers, key.Keysym))
            {
                _suppressed.Add(key.Keysym);
                return KeyMonitorVerdict.Block;
            }
        }

        return KeyMonitorVerdict.Pass;
    }

    private void RebuildGrabbedModifiers()
    {
        _grabbedModifiers.Clear();
        foreach (var client in _clients.Values)
        {
            _grabbedModifiers.UnionWith(client.Modifiers);
        }
    }
}
