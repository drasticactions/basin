namespace Basin.Capabilities;

/// <summary>
/// Supplies the drawing tablets, tools and pads that a backend sees, and their input. Device changes are events and per-event input goes to observers.
/// </summary>
public interface ITabletSource
{
    /// <summary>
    /// Copies the tablets that are present now.
    /// </summary>
    /// <param name="tablets">The span that receives the tablets.</param>
    /// <returns>The number of entries written, or -1 when the span is too small. The caller can then retry with a larger span.</returns>
    int EnumerateTablets(Span<TabletInfo> tablets);

    /// <summary>
    /// Copies the tools that are known now. A tool can stop being known when it leaves proximity.
    /// </summary>
    /// <param name="tools">The span that receives the tools.</param>
    /// <returns>The number of entries written, or -1 when the span is too small. The caller can then retry with a larger span.</returns>
    int EnumerateTools(Span<TabletToolInfo> tools);

    /// <summary>
    /// Copies the pads that are present now.
    /// </summary>
    /// <param name="pads">The span that receives the pads.</param>
    /// <returns>The number of entries written, or -1 when the span is too small. The caller can then retry with a larger span.</returns>
    int EnumeratePads(Span<TabletPadInfo> pads);

    /// <summary>
    /// Occurs when a tablet appears, with its description as the argument.
    /// </summary>
    event Action<TabletInfo>? TabletAdded;

    /// <summary>
    /// Occurs when a tablet goes away, with its id as the argument.
    /// </summary>
    event Action<ulong>? TabletRemoved;

    /// <summary>
    /// Occurs when the source sees a tool for the first time, before the first event of that tool reaches the observers.
    /// </summary>
    event Action<TabletToolInfo>? ToolAdded;

    /// <summary>
    /// Occurs when the source forgets a tool, with its id as the argument.
    /// </summary>
    event Action<ulong>? ToolRemoved;

    /// <summary>
    /// Occurs when a pad appears, with its description as the argument.
    /// </summary>
    event Action<TabletPadInfo>? PadAdded;

    /// <summary>
    /// Occurs when a pad goes away, with its id as the argument.
    /// </summary>
    event Action<ulong>? PadRemoved;

    /// <summary>
    /// Adds an observer that receives the tool and pad events.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(ITabletObserver observer);

    /// <summary>
    /// Removes an observer. It is safe to call from inside an event.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(ITabletObserver observer);
}
