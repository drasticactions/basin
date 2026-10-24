using Basin.Diagnostics;
using Wayland.Server;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

/// <summary>
/// The type-keyed registry of capabilities and protocol modules. Registration ends at <see cref="Freeze"/>, so that nothing resolves a capability on a frame.
/// </summary>
public sealed class BasinServices : IDisposable
{
    private readonly Dictionary<Type, object> _table = [];
    private readonly List<IProtocolModule> _queued = [];
    private readonly HashSet<string> _suppressed = [];
    private readonly Dictionary<string, IProtocolModule> _modules = [];
    private readonly List<IDisposable> _installed = [];
    private readonly List<IDisposable> _owned = [];
    private readonly List<Type> _unresolved = [];
    private readonly WlServerDisplay? _display;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinServices"/> class. Modules can create Wayland globals on the display.
    /// </summary>
    /// <param name="display">The display that the globals belong to.</param>
    /// <param name="loop">The event loop of the compositor.</param>
    public BasinServices(WlServerDisplay display, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);
        _display = display;
        Loop = loop;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinServices"/> class. The registry has no display, so only modules that create no global can install.
    /// </summary>
    /// <param name="loop">The event loop of the process.</param>
    public BasinServices(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
    }

    /// <summary>
    /// Gets the Wayland display. It throws <see cref="InvalidOperationException"/> when the registry has no display.
    /// </summary>
    public WlServerDisplay Display =>
        _display ?? throw new InvalidOperationException("This registry serves a process with no Wayland display. Install only modules that install no global.");

    /// <summary>
    /// Gets a value indicating whether the registry has a Wayland display.
    /// </summary>
    public bool HasDisplay => _display is not null;

    /// <summary>
    /// Gets the event loop that the compositor owns.
    /// </summary>
    public ICompositorEventLoop Loop { get; }

    private readonly List<Action> _seeds = [];

    /// <summary>
    /// Gets a value indicating whether <see cref="Freeze"/> completed. A frozen registry refuses all registration.
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Gets the installed modules, keyed by wire interface. It is empty before <see cref="Freeze"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IProtocolModule> Modules => _modules;

    /// <summary>
    /// Gets the capability types that a queued module consumes and that had no implementation at <see cref="Freeze"/>.
    /// </summary>
    public IReadOnlyList<Type> UnresolvedCapabilities => _unresolved;

    /// <summary>
    /// Registers the implementation of a service type. It throws <see cref="InvalidOperationException"/> when the type is already registered or the registry is frozen.
    /// </summary>
    /// <typeparam name="T">The service type that is the key.</typeparam>
    /// <param name="implementation">The implementation.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices Use<T>(T implementation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ThrowIfFrozen();
        if (!_table.TryAdd(typeof(T), implementation))
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already registered. Register each service once.");
        }

        return this;
    }

    /// <summary>
    /// Applies a capability pack.
    /// </summary>
    /// <param name="pack">The pack to register.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices With(ICapabilityPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        pack.Register(this);
        return this;
    }

    /// <summary>
    /// Registers an implementation only when the type has none. A module uses it, so that the consumer registration wins in any order.
    /// </summary>
    /// <typeparam name="T">The service type that is the key.</typeparam>
    /// <param name="implementation">The default implementation.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices UseDefault<T>(T implementation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        ThrowIfFrozen();
        _table.TryAdd(typeof(T), implementation);
        return this;
    }

    /// <summary>
    /// Queues a default that is created at <see cref="Freeze"/> only when the type has no implementation then. Use it for a default that is expensive to create.
    /// </summary>
    /// <typeparam name="T">The service type that is the key.</typeparam>
    /// <param name="create">The factory. It runs at most once.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices SeedDefault<T>(Func<T> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(create);
        ThrowIfFrozen();
        _seeds.Add(() =>
        {
            if (!_table.ContainsKey(typeof(T)))
            {
                _table[typeof(T)] = create();
            }
        });
        return this;
    }

    /// <summary>
    /// Looks up a service.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The implementation, or <see langword="null"/> when none is registered.</returns>
    public T? Find<T>()
        where T : class =>
        _table.TryGetValue(typeof(T), out var implementation) ? (T)implementation : null;

    /// <summary>
    /// Looks up a service that must exist. It throws <see cref="InvalidOperationException"/> when none is registered.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The implementation.</returns>
    public T Require<T>()
        where T : class =>
        Find<T>() ?? throw new InvalidOperationException($"No {typeof(T).Name} is registered.");

    /// <summary>
    /// Queues a module for <see cref="Freeze"/>. A module whose wire interface was removed with <see cref="Without"/> is ignored. A second module with the same wire interface throws.
    /// </summary>
    /// <param name="module">The module to queue.</param>
    /// <returns>This registry, for chained calls.</returns>
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
                    $"Two modules claim the wire interface '{module.WireInterface}'. Add only one module for each wire interface.");
            }
        }

        _queued.Add(module);
        return this;
    }

    /// <summary>
    /// Queues each module of a pack, in order.
    /// </summary>
    /// <param name="pack">The modules to queue.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices Install(ProtocolPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        foreach (var module in pack)
        {
            Install(module);
        }

        return this;
    }

    /// <summary>
    /// Removes a global entirely, both from the queue and from later installs. A removed global means "not permitted", which differs from a missing capability.
    /// </summary>
    /// <param name="wireInterface">The wire name of the global to remove.</param>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices Without(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        ThrowIfFrozen();
        _suppressed.Add(wireInterface);
        _queued.RemoveAll(m => m.WireInterface == wireInterface);
        return this;
    }

    /// <summary>
    /// Runs the queued defaults, then installs each queued module and ends registration. It throws <see cref="InvalidOperationException"/> when a module driver is missing or the registry is already frozen.
    /// </summary>
    /// <returns>This registry, for chained calls.</returns>
    public BasinServices Freeze()
    {
        ThrowIfFrozen();

        foreach (var seed in _seeds)
        {
            seed();
        }

        _seeds.Clear();
        foreach (var module in _queued)
        {
            module.SeedDefaults(this);
        }

        foreach (var module in _queued)
        {
            if (!module.ShouldInstall(this))
            {
                Log.Info($"services: {module.WireInterface} not advertised, its capability is absent");
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
                    Log.Debug($"services: no {capability.Name}, the protocols that use it degrade");
                }
            }
        }

        _queued.Clear();
        IsFrozen = true;
        return this;
    }

    /// <summary>
    /// Finds an installed module by wire interface.
    /// </summary>
    /// <param name="wireInterface">The wire name of the global.</param>
    /// <returns>The module, or <see langword="null"/> when no installed module has that name.</returns>
    public IProtocolModule? ModuleFor(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        return _modules.GetValueOrDefault(wireInterface);
    }

    /// <summary>
    /// Finds the first installed module of a type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    /// <returns>The module, or <see langword="null"/> when no installed module has that type.</returns>
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

    /// <summary>
    /// Makes the registry dispose an object at <see cref="Dispose"/>, after the installed globals. Use it for an object that a capability pack creates and no consumer keeps.
    /// </summary>
    /// <param name="owned">The object to dispose with the registry.</param>
    /// <returns>This registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owned"/> is <see langword="null"/>.</exception>
    public BasinServices Own(IDisposable owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        _owned.Add(owned);
        return this;
    }

    /// <summary>
    /// Tells each registered implementation, module and installed global that implements <see cref="IRendererBound"/> about a renderer replacement, once per object.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
    public void NotifyRendererReplaced(RendererReplacement replacement) => Notify(replacement, rebuilt: false);

    /// <summary>
    /// Calls <see cref="IRendererBound.OnRendererRebuilt"/> on the same objects as <see cref="NotifyRendererReplaced"/>, once per object.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
    public void NotifyRendererRebuilt(RendererReplacement replacement) => Notify(replacement, rebuilt: true);

    private void Notify(RendererReplacement replacement, bool rebuilt)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var told = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var implementation in _table.Values)
        {
            Tell(implementation);
        }

        foreach (var module in _modules.Values)
        {
            Tell(module);
        }

        foreach (var global in _installed)
        {
            Tell(global);
        }

        void Tell(object candidate)
        {
            if (candidate is IRendererBound bound && told.Add(candidate))
            {
                if (rebuilt)
                {
                    bound.OnRendererRebuilt(replacement);
                }
                else
                {
                    bound.OnRendererReplaced(replacement);
                }
            }
        }
    }

    /// <summary>
    /// Disposes the installed globals in reverse install order, then the objects given to <see cref="Own"/>, and clears the registry.
    /// </summary>
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
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
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
                $"{module.WireInterface} needs {article} {driver.Name} and none is registered. " +
                $"Register one before Freeze, or drop the protocol with Without(\"{module.WireInterface}\").");
        }
    }

    private void ThrowIfFrozen()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("The service registry is frozen. Register services before Freeze.");
        }
    }
}
