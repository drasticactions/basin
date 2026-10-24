namespace Basin.Ipc;

/// <summary>
/// Receives the events that it subscribed to on an <see cref="IpcEventBus"/>.
/// </summary>
public interface IIpcEventSubscriber
{
    /// <summary>
    /// Takes one event. The bus calls it on the compositor thread.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="message">The whole event envelope as UTF-8 JSON, with <c>event</c> and <c>data</c>. The span is valid only during the call.</param>
    void OnEvent(string name, ReadOnlySpan<byte> message);
}
