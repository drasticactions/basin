using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class UISurfaceObservers
{
    private readonly ObserverList<IUISurfaceObserver> _observers = new();

    public void Add(IUISurfaceObserver observer) => _observers.Add(observer);

    public void Remove(IUISurfaceObserver observer) => _observers.Remove(observer);

    public void Damaged(IUISurface surface, PixmanRegion32 damage)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSurfaceDamaged(surface, damage);
        }

        _observers.EndDispatch();
    }

    public void Destroyed(IUISurface surface)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSurfaceDestroyed(surface);
        }

        _observers.EndDispatch();
    }
}
