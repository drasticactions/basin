using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Holds a list of <see cref="ITabletObserver"/> instances for an implementation of <see cref="ITabletSource"/>. Observers can add or remove observers during a dispatch.
/// </summary>
public sealed class TabletObservers
{
    private readonly ObserverList<ITabletObserver> _observers = new();

    /// <summary>
    /// Adds an observer.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    public void Add(ITabletObserver observer) => _observers.Add(observer);

    /// <summary>
    /// Removes an observer.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void Remove(ITabletObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Sends a proximity change to each observer.
    /// </summary>
    /// <param name="toolId">The id of the tool.</param>
    /// <param name="tabletId">The id of the tablet.</param>
    /// <param name="inProximity"><see langword="true"/> when the tool enters, <see langword="false"/> when it leaves.</param>
    public void ToolProximity(ulong toolId, ulong tabletId, bool inProximity)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolProximity(toolId, tabletId, inProximity);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Sends the axis values of a tool to each observer.
    /// </summary>
    /// <param name="toolId">The id of the tool.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axes">The axis values.</param>
    public void ToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolAxis(toolId, timeMs, axes);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Sends a tool button change to each observer.
    /// </summary>
    /// <param name="toolId">The id of the tool.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux input event code of the button.</param>
    /// <param name="pressed"><see langword="true"/> when the button is pressed.</param>
    public void ToolButton(ulong toolId, uint timeMs, uint button, bool pressed)
    {
        var count = _observers.BeginDispatch();
        for (var i = 0; i < count; i++)
        {
            _observers[i]?.OnToolButton(toolId, timeMs, button, pressed);
        }

        _observers.EndDispatch();
    }

    /// <summary>
    /// Sends a pad event to each observer.
    /// </summary>
    /// <param name="padId">The id of the pad.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="padEvent">The control that changed and its value.</param>
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
