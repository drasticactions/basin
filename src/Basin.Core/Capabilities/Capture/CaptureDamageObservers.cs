using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class CaptureDamageObservers
{
    private readonly ObserverList<ICaptureDamageObserver> _observers = new();

    public void Add(ICaptureDamageObserver observer) => _observers.Add(observer);

    public void Remove(ICaptureDamageObserver observer) => _observers.Remove(observer);

    public void Damaged(IOutput output, in Box damage)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnSourceDamaged(output, damage);
        }

        _observers.EndDispatch();
    }

    public void CursorChanged()
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnCursorChanged();
        }

        _observers.EndDispatch();
    }
}
