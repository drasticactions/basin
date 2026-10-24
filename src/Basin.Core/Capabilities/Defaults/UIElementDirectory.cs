namespace Basin.Capabilities.Defaults;

/// <summary>
/// Combines the element locators of several UI hosts into one <see cref="IUIElementLocator"/>. The newest locator answers first.
/// </summary>
public sealed class UIElementDirectory : IUIElementLocator
{
    private readonly List<IUIElementLocator> _sources = [];

    /// <summary>
    /// Adds a UI host's locator. A source that is already added is ignored.
    /// </summary>
    /// <param name="source">The locator to add.</param>
    public void Add(IUIElementLocator source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Contains(source))
        {
            _sources.Add(source);
        }
    }

    /// <summary>
    /// Removes a UI host's locator.
    /// </summary>
    /// <param name="source">The locator to remove.</param>
    public void Remove(IUIElementLocator source) => _sources.Remove(source);

    /// <inheritdoc/>
    public bool TryLocate(string name, out Box box)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var i = _sources.Count - 1; i >= 0; i--)
        {
            if (_sources[i].TryLocate(name, out box))
            {
                return true;
            }
        }

        box = default;
        return false;
    }
}
