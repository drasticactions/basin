using System.Collections;

namespace Basin;

public sealed class ProtocolPack : IReadOnlyList<IProtocolModule>
{
    private readonly List<IProtocolModule> _modules;

    public ProtocolPack()
        : this([])
    {
    }

    public ProtocolPack(IEnumerable<IProtocolModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _modules = [];
        foreach (var module in modules)
        {
            Add(module);
        }
    }

    public int Count => _modules.Count;

    public IProtocolModule this[int index] => _modules[index];

    public static ProtocolPack operator +(ProtocolPack left, ProtocolPack right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new ProtocolPack(left.Concat(right));
    }

    public ProtocolPack With(IProtocolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new ProtocolPack(_modules.Append(module));
    }

    public ProtocolPack Without(string wireInterface)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireInterface);
        return new ProtocolPack(_modules.Where(m => m.WireInterface != wireInterface));
    }

    public bool Contains(string wireInterface) =>
        _modules.Any(m => m.WireInterface == wireInterface);

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
                    $"two modules claim the wire interface '{module.WireInterface}'", nameof(module));
            }
        }

        _modules.Add(module);
    }
}
