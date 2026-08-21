using Wayland;

namespace Basin.WindowManager;

public sealed class OutputScales
{
    private readonly Dictionary<WlOutput, Entry> _byProxy = [];

    public OutputScales(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        var registry = wm.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface != "wl_output")
            {
                return;
            }

            var output = registry.Bind<WlOutput>(e.Name, Math.Min(e.Version, 4));
            var entry = new Entry(e.Name);
            _byProxy[output] = entry;
            output.Scale += (_, se) => entry.Scale = Math.Max(se.Factor, 1);
        };
        registry.GlobalRemove += (_, e) =>
        {
            foreach (var (proxy, entry) in _byProxy)
            {
                if (entry.Name == e.Name)
                {
                    _byProxy.Remove(proxy);
                    if (!proxy.IsDestroyed)
                    {
                        proxy.Dispose();
                    }

                    break;
                }
            }
        };
    }

    public int ScaleFor(WlOutput proxy) => _byProxy.TryGetValue(proxy, out var entry) ? entry.Scale : 1;

    public int ScaleForName(uint name)
    {
        foreach (var entry in _byProxy.Values)
        {
            if (entry.Name == name)
            {
                return entry.Scale;
            }
        }

        return 1;
    }

    public WlOutput? ProxyForName(uint name)
    {
        foreach (var (proxy, entry) in _byProxy)
        {
            if (entry.Name == name)
            {
                return proxy;
            }
        }

        return null;
    }

    private sealed class Entry(uint name)
    {
        public uint Name { get; } = name;

        public int Scale { get; set; } = 1;
    }
}
