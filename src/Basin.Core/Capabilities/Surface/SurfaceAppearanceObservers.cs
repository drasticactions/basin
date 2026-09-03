namespace Basin.Capabilities;

public sealed class SurfaceAppearanceObservers
{
    private readonly ObserverList<ISurfaceAppearanceObserver> _observers = new();

    public void Add(ISurfaceAppearanceObserver observer) => _observers.Add(observer);

    public void Remove(ISurfaceAppearanceObserver observer) => _observers.Remove(observer);

    public void Changed(Surface surface)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.AppearanceChanged(surface);
        }

        _observers.EndDispatch();
    }
}
