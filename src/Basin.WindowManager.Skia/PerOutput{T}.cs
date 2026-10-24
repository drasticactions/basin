using Wayland;

namespace Basin.WindowManager.Skia;

/// <summary>
/// A table of one item per output, for example one bar or wallpaper surface per output. It makes items for new outputs and disposes the items of outputs that went away.
/// </summary>
/// <typeparam name="T">The item type, which the table disposes.</typeparam>
public sealed class PerOutput<T>
    where T : class, IDisposable
{
    private readonly ManagerSurfaceHost? _host;
    private readonly Func<WmOutput, WlOutput, T> _create;
    private readonly Dictionary<WmOutput, T> _items = [];
    private readonly List<WmOutput> _stale = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PerOutput{T}"/> class.
    /// </summary>
    /// <param name="host">The host that maps outputs to proxies, or <see langword="null"/> for a table that never makes items.</param>
    /// <param name="create">The factory for an output's item, with the river output and its bound proxy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is <see langword="null"/>.</exception>
    public PerOutput(ManagerSurfaceHost? host, Func<WmOutput, WlOutput, T> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _host = host;
        _create = create;
    }

    /// <summary>
    /// Gets the number of items.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets the items, without allocation.
    /// </summary>
    public Dictionary<WmOutput, T>.ValueCollection Values => _items.Values;

    /// <summary>
    /// Occurs before an item is disposed, with its output and the item.
    /// </summary>
    public event Action<WmOutput, T>? Removing;

    /// <summary>
    /// Gets the item of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="item">The item, or <see langword="null"/> when there is none.</param>
    /// <returns><see langword="true"/> when the output has an item.</returns>
    public bool TryGetValue(WmOutput output, out T item) => _items.TryGetValue(output, out item!);

    /// <summary>
    /// Tests whether an output has an item.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when the output has an item.</returns>
    public bool ContainsKey(WmOutput output) => _items.ContainsKey(output);

    /// <summary>
    /// Gets the item of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The item, or <see langword="null"/> when there is none.</returns>
    public T? GetValueOrDefault(WmOutput output) => _items.GetValueOrDefault(output);

    /// <summary>
    /// Gets an enumerator over the outputs and their items, without allocation.
    /// </summary>
    /// <returns>The dictionary enumerator.</returns>
    public Dictionary<WmOutput, T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Makes an item for each output that has a bound proxy and no item, once the layer shell is bound. It disposes the items of removed outputs, or all items when not wanted.
    /// </summary>
    /// <param name="outputs">The outputs that are present.</param>
    /// <param name="wanted"><see langword="false"/> to dispose every item and make none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is <see langword="null"/>.</exception>
    public void Reconcile(IReadOnlyList<WmOutput> outputs, bool wanted = true)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        if (wanted && _host is { LayerShell: not null } host)
        {
            foreach (var output in outputs)
            {
                if (!output.IsRemoved && !_items.ContainsKey(output) && host.ProxyFor(output) is { } proxy)
                {
                    _items[output] = _create(output, proxy);
                }
            }
        }

        foreach (var (output, item) in _items)
        {
            if (output.IsRemoved || !wanted)
            {
                _stale.Add(output);
            }
        }

        foreach (var output in _stale)
        {
            Remove(output);
        }

        _stale.Clear();
    }

    /// <summary>
    /// Raises <see cref="Removing"/> and disposes the item of an output.
    /// </summary>
    /// <param name="output">The output whose item to remove.</param>
    /// <returns><see langword="true"/> when the output had an item.</returns>
    public bool Remove(WmOutput output)
    {
        if (!_items.Remove(output, out var item))
        {
            return false;
        }

        Removing?.Invoke(output, item);
        item.Dispose();
        return true;
    }

    /// <summary>
    /// Removes and disposes every item, raising <see cref="Removing"/> for each.
    /// </summary>
    public void Clear()
    {
        foreach (var output in _items.Keys)
        {
            _stale.Add(output);
        }

        foreach (var output in _stale)
        {
            Remove(output);
        }

        _stale.Clear();
    }
}
