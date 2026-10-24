namespace Basin.Capabilities;

/// <summary>
/// Receives a notification when the stacking order of toplevels changes.
/// </summary>
public interface IToplevelStackObserver
{
    /// <summary>
    /// The stack calls this method after the order changes. Call <see cref="IToplevelStack.Enumerate"/> to read the new order.
    /// </summary>
    void OnToplevelStackChanged();
}
