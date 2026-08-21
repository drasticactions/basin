using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public sealed class TabletObservers
{
    private readonly ObserverList<ITabletObserver> _observers = new();

    public void Add(ITabletObserver observer) => _observers.Add(observer);

    public void Remove(ITabletObserver observer) => _observers.Remove(observer);

    public void ToolProximity(ulong toolId, ulong tabletId, bool inProximity)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolProximity(toolId, tabletId, inProximity);
        }

        _observers.EndDispatch();
    }

    public void ToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolAxis(toolId, timeMs, axes);
        }

        _observers.EndDispatch();
    }

    public void ToolButton(ulong toolId, uint timeMs, uint button, bool pressed)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolButton(toolId, timeMs, button, pressed);
        }

        _observers.EndDispatch();
    }

    public void PadEvent(ulong padId, uint timeMs, TabletPadEvent padEvent)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnPadEvent(padId, timeMs, padEvent);
        }

        _observers.EndDispatch();
    }
}
