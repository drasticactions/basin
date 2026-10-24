using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Fills a <see cref="ToplevelSceneIndex"/> from the notifications of a toplevel model, and tells the stack about each change.
/// </summary>
public sealed class ToplevelCaptureIndexObserver : IToplevelObserver
{
    private readonly IToplevelModel _toplevels;
    private readonly ToplevelSceneIndex _index;
    private readonly SceneToplevelStack _stack;
    private readonly Func<Surface, ToplevelCaptureTrees?> _resolve;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToplevelCaptureIndexObserver"/> class. The caller adds it to the model.
    /// </summary>
    /// <param name="toplevels">The model to read toplevel info from.</param>
    /// <param name="index">The index to fill.</param>
    /// <param name="stack">The stack to notify after each index change.</param>
    /// <param name="resolve">A function that finds the scene nodes of a surface, or returns <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ToplevelCaptureIndexObserver(
        IToplevelModel toplevels,
        ToplevelSceneIndex index,
        SceneToplevelStack stack,
        Func<Surface, ToplevelCaptureTrees?> resolve)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(resolve);
        _toplevels = toplevels;
        _index = index;
        _stack = stack;
        _resolve = resolve;
    }

    /// <summary>
    /// Resolves the scene nodes of the new toplevel and adds them to the index when the content node exists.
    /// </summary>
    /// <param name="toplevelId">The id of the new toplevel.</param>
    public void OnToplevelAdded(ulong toplevelId) => Refresh(toplevelId);

    /// <summary>
    /// Resolves the scene nodes of a toplevel that the index does not hold yet. An entry that exists stays as it is.
    /// </summary>
    /// <param name="toplevelId">The id of the changed toplevel.</param>
    public void OnToplevelChanged(ulong toplevelId) => Refresh(toplevelId);

    /// <summary>
    /// Removes the toplevel from the index and notifies the stack.
    /// </summary>
    /// <param name="toplevelId">The id of the removed toplevel.</param>
    public void OnToplevelRemoved(ulong toplevelId)
    {
        _index.Remove(toplevelId);
        _stack.RaiseChanged();
    }

    private void Refresh(ulong toplevelId)
    {
        if (_index.TryGet(toplevelId, out _))
        {
            return;
        }

        if (!_toplevels.TryGet(toplevelId, out var info) || info.Surface is not { } surface)
        {
            return;
        }

        if (_resolve(surface) is { Content: not null } trees)
        {
            _index.Set(toplevelId, trees);
            _stack.RaiseChanged();
        }
    }
}
