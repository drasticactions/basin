namespace Basin.Ipc;

/// <summary>
/// Feeds one or more library events from a capability. The bus attaches it when its first subscriber arrives and detaches it when the last one leaves.
/// </summary>
public abstract class IpcEventSource
{
    /// <summary>
    /// Gets a value indicating whether the source has at least one subscriber and watches its capability.
    /// </summary>
    public bool IsAttached { get; private set; }

    internal int Demand { get; set; }

    internal void SetAttached(bool attached)
    {
        if (attached == IsAttached)
        {
            return;
        }

        IsAttached = attached;
        if (attached)
        {
            Attach();
        }
        else
        {
            Detach();
        }
    }

    /// <summary>
    /// Checks the options of a subscription before the bus accepts it. The default accepts all options.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="options">The subscription's options, or <see langword="null"/> when it has none.</param>
    /// <returns>The text of the refusal, or <see langword="null"/> to accept.</returns>
    protected internal virtual string? Validate(string name, System.Text.Json.JsonElement? options) => null;

    /// <summary>
    /// Tells the source that a subscriber or its options changed for an event. The default does nothing.
    /// </summary>
    /// <param name="name">The event name.</param>
    protected internal virtual void SubscribersChanged(string name)
    {
    }

    /// <summary>
    /// Starts to watch the capability. The bus calls it when the first subscriber arrives.
    /// </summary>
    protected abstract void Attach();

    /// <summary>
    /// Stops watching the capability. The bus calls it when the last subscriber leaves or the bus is disposed.
    /// </summary>
    protected abstract void Detach();
}
