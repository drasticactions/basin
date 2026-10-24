namespace Basin.Capabilities;

/// <summary>
/// Reports seat input as trace events for diagnostics.
/// </summary>
public interface IInputDiagnostics
{
    /// <summary>
    /// Starts to send seat input events to <paramref name="sink"/>. The watch combines pointer motion into one event per frame.
    /// </summary>
    /// <param name="motion"><see langword="true"/> to also receive pointer motion.</param>
    /// <param name="sink">The callback for each event. It runs on the compositor thread.</param>
    /// <returns>A handle that stops the watch when disposed.</returns>
    IDisposable WatchInput(bool motion, Action<InputTraceEvent> sink);
}
