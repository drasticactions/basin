namespace Basin.Capabilities.Defaults;

/// <summary>
/// Combines the popups of several shells into one <see cref="IPopupDirectory"/>.
/// </summary>
public sealed class PopupDirectory : IPopupDirectory
{
    private readonly List<IPopupDirectory> _sources = [];

    /// <summary>
    /// Adds a shell's popup directory. A source that is already added is ignored.
    /// </summary>
    /// <param name="source">The directory to add.</param>
    public void Add(IPopupDirectory source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Contains(source))
        {
            _sources.Add(source);
        }
    }

    /// <summary>
    /// Removes a shell's popup directory.
    /// </summary>
    /// <param name="source">The directory to remove.</param>
    public void Remove(IPopupDirectory source) => _sources.Remove(source);

    /// <inheritdoc/>
    public void CollectPopups(Surface owner, List<Surface> into)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(into);
        foreach (var source in _sources)
        {
            source.CollectPopups(owner, into);
        }
    }
}
