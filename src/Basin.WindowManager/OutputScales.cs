using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// The integer scale of each <c>wl_output</c> global, through a registry of its own. It binds each output as the global arrives and drops it when the global goes away.
/// </summary>
public sealed class OutputScales
{
    private readonly Dictionary<WlOutput, Entry> _byProxy = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputScales"/> class. The outputs arrive on the manager's next dispatch.
    /// </summary>
    /// <param name="wm">The manager whose connection this object binds on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the scale of an output that this object bound.
    /// </summary>
    /// <param name="proxy">An output proxy from this object.</param>
    /// <returns>The scale, or 1 for an unknown output.</returns>
    public int ScaleFor(WlOutput proxy) => _byProxy.TryGetValue(proxy, out var entry) ? entry.Scale : 1;

    /// <summary>
    /// Gets the scale of an output by its registry name.
    /// </summary>
    /// <param name="name">The registry name, for example <see cref="WmOutput.WlOutputName"/>.</param>
    /// <returns>The scale, or 1 for an unknown output.</returns>
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

    /// <summary>
    /// Gets the bound output for a registry name, for layer surfaces and capture sessions.
    /// </summary>
    /// <param name="name">The registry name, for example <see cref="WmOutput.WlOutputName"/>.</param>
    /// <returns>The proxy, or <see langword="null"/> when no output has the name.</returns>
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
