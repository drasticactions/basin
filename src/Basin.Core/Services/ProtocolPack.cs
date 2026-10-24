using System.Collections;

namespace Basin;

/// <summary>
/// An immutable list of protocol modules with unique wire interfaces. Each change makes a new pack.
/// </summary>
public sealed class ProtocolPack : IReadOnlyList<IProtocolModule>
{
    private readonly List<IProtocolModule> _modules;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolPack"/> class. The pack is empty.
    /// </summary>
    public ProtocolPack()
        : this([])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolPack"/> class. It throws <see cref="ArgumentException"/> when two modules have the same wire interface.
    /// </summary>
    /// <param name="modules">The modules, in install order.</param>
    public ProtocolPack(IEnumerable<IProtocolModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _modules = [];
        foreach (var module in modules)
        {
            Add(module);
        }
    }

    /// <inheritdoc/>
    public int Count => _modules.Count;

    /// <inheritdoc/>
    public IProtocolModule this[int index] => _modules[index];

    /// <summary>
    /// Joins two packs. It throws <see cref="ArgumentException"/> when both packs contain the same wire interface.
    /// </summary>
    /// <param name="left">The pack whose modules come first.</param>
    /// <param name="right">The pack whose modules come second.</param>
    /// <returns>A new pack with the modules of both.</returns>
    public static ProtocolPack operator +(ProtocolPack left, ProtocolPack right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new ProtocolPack(left.Concat(right));
    }

    /// <summary>
    /// Adds a module at the end. It throws <see cref="ArgumentException"/> when the pack already has its wire interface.
    /// </summary>
    /// <param name="module">The module to add.</param>
    /// <returns>A new pack.</returns>
    public ProtocolPack With(IProtocolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new ProtocolPack(_modules.Append(module));
    }

    /// <summary>
    /// Removes the module with a wire interface. A name that is not in the pack is ignored.
    /// </summary>
    /// <param name="wireInterface">The wire name to remove.</param>
    /// <returns>A new pack.</returns>
    public ProtocolPack Without(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        return new ProtocolPack(_modules.Where(m => m.WireInterface != wireInterface));
    }

    /// <summary>
    /// Tests whether a module in the pack has a wire interface.
    /// </summary>
    /// <param name="wireInterface">The wire name to find.</param>
    /// <returns><see langword="true"/> when a module has that wire name.</returns>
    public bool Contains(string wireInterface) =>
        _modules.Any(m => m.WireInterface == wireInterface);

    /// <inheritdoc/>
    public IEnumerator<IProtocolModule> GetEnumerator() => _modules.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Add(IProtocolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        foreach (var existing in _modules)
        {
            if (existing.WireInterface == module.WireInterface)
            {
                throw new ArgumentException(
                    $"Two modules claim the wire interface '{module.WireInterface}'. Add only one module for each wire interface.", nameof(module));
            }
        }

        _modules.Add(module);
    }
}
