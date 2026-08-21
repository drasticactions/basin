using Basin.Diagnostics;
using Wayland.Server;

namespace Basin;

public sealed class BasinServices : IDisposable
{
    private readonly Dictionary<Type, object> _table = [];
    private readonly List<IProtocolModule> _queued = [];
    private readonly HashSet<string> _suppressed = [];
    private readonly Dictionary<string, IProtocolModule> _modules = [];
    private readonly List<IDisposable> _installed = [];
    private readonly List<Type> _unresolved = [];
    private bool _disposed;

    public BasinServices(WlServerDisplay display, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        Display = display;
        Loop = loop;
    }

    public WlServerDisplay Display { get; }

    public ICompositorEventLoop Loop { get; }

    public bool IsFrozen { get; private set; }

    public IReadOnlyDictionary<string, IProtocolModule> Modules => _modules;

    public IReadOnlyList<Type> UnresolvedCapabilities => _unresolved;

    public BasinServices Use<T>(T implementation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ThrowIfFrozen();
        if (!_table.TryAdd(typeof(T), implementation))
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already registered");
        }

        return this;
    }

    public BasinServices With(ICapabilityPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        pack.Register(this);
        return this;
    }

    public BasinServices UseDefault<T>(T implementation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ThrowIfFrozen();
        _table.TryAdd(typeof(T), implementation);
        return this;
    }

    public T? Find<T>()
        where T : class =>
        _table.TryGetValue(typeof(T), out var implementation) ? (T)implementation : null;

    public T Require<T>()
        where T : class =>
        Find<T>() ?? throw new InvalidOperationException($"no {typeof(T).Name} is registered");

    public BasinServices Install(IProtocolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ThrowIfFrozen();
        if (_suppressed.Contains(module.WireInterface))
        {
            return this;
        }

        foreach (var queued in _queued)
        {
            if (queued.WireInterface == module.WireInterface)
            {
                throw new InvalidOperationException(
                    $"two modules claim the wire interface '{module.WireInterface}'");
            }
        }

        _queued.Add(module);
        return this;
    }

    public BasinServices Install(ProtocolPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        foreach (var module in pack)
        {
            Install(module);
        }

        return this;
    }

    public BasinServices Without(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        ThrowIfFrozen();
        _suppressed.Add(wireInterface);
        _queued.RemoveAll(m => m.WireInterface == wireInterface);
        return this;
    }

    public BasinServices Freeze()
    {
        ThrowIfFrozen();

        foreach (var module in _queued)
        {
            module.SeedDefaults(this);
        }

        foreach (var module in _queued)
        {
            if (!module.ShouldInstall(this))
            {
                BasinLog.Info($"{module.WireInterface} not advertised: its capability is absent");
                continue;
            }

            RequireDrivers(module);
            _installed.Add(module.Install(this));
            _modules[module.WireInterface] = module;
        }

        foreach (var module in _queued)
        {
            foreach (var capability in module.Capabilities)
            {
                if (!_table.ContainsKey(capability) && !_unresolved.Contains(capability))
                {
                    _unresolved.Add(capability);
                    BasinLog.Debug($"no implementation for {capability.Name}; protocols consuming it degrade");
                }
            }
        }

        _queued.Clear();
        IsFrozen = true;
        return this;
    }

    public IProtocolModule? ModuleFor(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        return _modules.GetValueOrDefault(wireInterface);
    }

    public T? Module<T>()
        where T : class, IProtocolModule
    {
        foreach (var module in _modules.Values)
        {
            if (module is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = _installed.Count - 1; i >= 0; i--)
        {
            _installed[i].Dispose();
        }

        _installed.Clear();
        _modules.Clear();
        _table.Clear();
    }

    private void RequireDrivers(IProtocolModule module)
    {
        foreach (var driver in module.Drivers)
        {
            if (_table.ContainsKey(driver))
            {
                continue;
            }

            var article = "AEIOU".Contains(driver.Name[0]) ? "an" : "a";
            throw new InvalidOperationException(
                $"{module.WireInterface} needs {article} {driver.Name} and none is registered.{Environment.NewLine}" +
                $"Register one before Freeze, or drop the protocol with Without(\"{module.WireInterface}\").");
        }
    }

    private void ThrowIfFrozen()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("the service registry is frozen; register before Freeze()");
        }
    }
}
