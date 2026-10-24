namespace Basin.Themes.MsStyles;

/// <summary>
/// Counts the items that a theme reader skipped or did not recognize, for example unknown resource types or property IDs. No count is fatal.
/// </summary>
public sealed class MsStylesDiagnostics
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the count of each item. The key is the kind and the item, joined as <c>kind:what</c>.
    /// </summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>
    /// Gets the number of calls to <see cref="Add"/>, repeats included.
    /// </summary>
    public int Total { get; private set; }

    /// <summary>
    /// Adds the counts of every item of one kind.
    /// </summary>
    /// <param name="kind">The kind, for example <c>resource-type</c>.</param>
    /// <returns>The sum of the counts whose key starts with <paramref name="kind"/> and a colon. It is 0 when no item of the kind is counted.</returns>
    public int CountOf(string kind)
    {
        var prefix = kind + ":";
        var total = 0;
        foreach (var (key, count) in _counts)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                total += count;
            }
        }

        return total;
    }

    /// <summary>
    /// Counts an item only when it is not counted yet. A second call for the same item changes nothing.
    /// </summary>
    /// <param name="kind">The kind of the item.</param>
    /// <param name="what">The item, for example a property name.</param>
    public void AddOnce(string kind, string what)
    {
        if (!_counts.ContainsKey(kind + ":" + what))
        {
            Add(kind, what);
        }
    }

    /// <summary>
    /// Adds 1 to the count of an item and to <see cref="Total"/>.
    /// </summary>
    /// <param name="kind">The kind of the item.</param>
    /// <param name="what">The item, for example a property name.</param>
    public void Add(string kind, string what)
    {
        var key = kind + ":" + what;
        _counts[key] = _counts.TryGetValue(key, out var count) ? count + 1 : 1;
        Total++;
    }

    /// <summary>
    /// Formats every count on its own line as <c>kind:what xN</c>, sorted by key.
    /// </summary>
    /// <returns>The counts, one line each. It is empty when nothing is counted.</returns>
    public override string ToString() =>
        string.Join(Environment.NewLine, _counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} x{p.Value}"));
}
