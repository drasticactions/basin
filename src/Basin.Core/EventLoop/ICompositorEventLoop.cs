namespace Basin;

/// <summary>
/// The single-threaded loop that the compositor owns: protocol dispatch, descriptors, timers, signals, idle callbacks and deferred destruction. All members must be called from the loop thread.
/// </summary>
public interface ICompositorEventLoop
{
    /// <summary>
    /// Runs the queued idle callbacks and deferred destroys, then waits for events and runs their handlers.
    /// </summary>
    /// <param name="timeoutMs">The maximum wait in milliseconds. 0 returns at once and -1 waits with no limit.</param>
    void Dispatch(int timeoutMs);

    /// <summary>
    /// Watches a file descriptor. The caller keeps ownership of the descriptor and must remove the source before it closes it.
    /// </summary>
    /// <param name="fd">The descriptor to watch.</param>
    /// <param name="events">The conditions to watch.</param>
    /// <param name="handler">The callback, with the descriptor and the conditions that occurred.</param>
    /// <returns>The source that controls the watch.</returns>
    IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler);

    /// <summary>
    /// Makes a timer that is disarmed until <see cref="IEventSource.UpdateTimer"/> arms it.
    /// </summary>
    /// <param name="handler">The callback that runs each time the timer fires.</param>
    /// <returns>The timer source.</returns>
    IEventSource AddTimer(Action handler);

    /// <summary>
    /// Handles a POSIX signal on the loop thread instead of in the signal handler. The default action of the signal is canceled while the source exists.
    /// </summary>
    /// <param name="signalNumber">The signal number, for example <see cref="Signal.Terminate"/>.</param>
    /// <param name="handler">The callback, with the signal number. It runs once for each delivery.</param>
    /// <returns>The signal source.</returns>
    IEventSource AddSignal(int signalNumber, Action<int> handler);

    /// <summary>
    /// Queues a callback that runs once, before the next wait for events.
    /// </summary>
    /// <param name="handler">The callback.</param>
    void AddIdle(Action handler);

    /// <summary>
    /// Runs the queued idle callbacks and deferred destroys now, with no wait for events.
    /// </summary>
    void DispatchIdle();

    /// <summary>
    /// Gets a descriptor that becomes readable when the loop has events. An outer loop can watch it.
    /// </summary>
    int Fd { get; }

    /// <summary>
    /// Disposes an object after the current dispatch, after the idle callbacks. Use it for an object that is destroyed from inside its own callback.
    /// </summary>
    /// <param name="victim">The object to dispose later.</param>
    void DeferDestroy(IDisposable victim);
}
