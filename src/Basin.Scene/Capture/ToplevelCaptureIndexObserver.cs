using Basin.Capabilities;

namespace Basin.Scene;

public sealed class ToplevelCaptureIndexObserver : IToplevelObserver
{
    private readonly IToplevelModel _toplevels;
    private readonly ToplevelSceneIndex _index;
    private readonly SceneToplevelStack _stack;
    private readonly Func<Surface, ToplevelCaptureTrees?> _resolve;

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

    public void OnToplevelAdded(ulong toplevelId) => Refresh(toplevelId);

    public void OnToplevelChanged(ulong toplevelId) => Refresh(toplevelId);

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
