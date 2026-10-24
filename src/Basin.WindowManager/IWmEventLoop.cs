namespace Basin.WindowManager;

/// <summary>
/// The loop that <see cref="RiverWindowManager.Run"/> drives, for the fds, timers and idle work of a manager. Every callback runs on the loop thread.
/// </summary>
public interface IWmEventLoop
{
    /// <summary>
    /// Watches an fd. The loop does not take ownership of the fd and does not close it.
    /// </summary>
    /// <param name="fd">The fd to poll.</param>
    /// <param name="events">The readiness to wait for. Hangup and error are always reported.</param>
    /// <param name="handler">The callback, with the fd and the readiness that the poll reported.</param>
    /// <returns>The source. Remove it before the fd closes.</returns>
    IWmEventSource AddFd(int fd, WmFdReadiness events, Action<int, WmFdReadiness> handler);

    /// <summary>
    /// Adds a one-shot timer. It is disarmed until <see cref="IWmEventSource.UpdateTimer"/> arms it, and it disarms again after it fires.
    /// </summary>
    /// <param name="handler">The callback that runs when the timer expires.</param>
    /// <returns>The source that arms, disarms and removes the timer.</returns>
    IWmEventSource AddTimer(Action handler);

    /// <summary>
    /// Runs a callback once, before the loop next waits. A callback that adds idle work makes that work run in the same drain.
    /// </summary>
    /// <param name="handler">The callback to run.</param>
    void AddIdle(Action handler);
}
