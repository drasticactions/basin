namespace Basin.Capabilities.Defaults;

public sealed class PopupDirectory : IPopupDirectory
{
    private readonly List<IPopupDirectory> _sources = [];

    public void Add(IPopupDirectory source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Contains(source))
        {
            _sources.Add(source);
        }
    }

    public void Remove(IPopupDirectory source) => _sources.Remove(source);

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
