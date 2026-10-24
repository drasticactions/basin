namespace Basin.WindowManager.Ipc;

/// <summary>
/// Adapts a window manager's <see cref="IWmEventLoop"/> to <see cref="ICompositorEventLoop"/>, so basin services that need a loop can run in a manager. It cannot dispatch, has no fd and has no signal sources.
/// </summary>
/// <param name="loop">The manager's loop, which keeps running the dispatch.</param>
public sealed class WmIpcLoop(IWmEventLoop loop) : ICompositorEventLoop
{
    /// <summary>
    /// Gets nothing. This loop has no fd of its own.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public int Fd => throw new NotSupportedException("a window manager's loop has no fd of its own");

    /// <summary>
    /// Does not dispatch. The window manager runs its own loop.
    /// </summary>
    /// <param name="timeoutMs">Not used.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public void Dispatch(int timeoutMs) => throw new NotSupportedException("the window manager runs its own loop");

    /// <inheritdoc/>
    public IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Source(loop.AddFd(fd, (WmFdReadiness)events, (ready, readiness) => handler(ready, (FdReadiness)readiness)));
    }

    /// <summary>
    /// Makes a timer that is disarmed until <see cref="IEventSource.UpdateTimer"/> arms it. It fires once for each arm.
    /// </summary>
    /// <param name="handler">The callback that runs when the timer fires.</param>
    /// <returns>The timer source.</returns>
    public IEventSource AddTimer(Action handler) => new Source(loop.AddTimer(handler));

    /// <summary>
    /// Does not add a signal source. This loop has none.
    /// </summary>
    /// <param name="signalNumber">The signal number, which is ignored.</param>
    /// <param name="handler">The callback, which never runs.</param>
    /// <returns>Nothing. The method always throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public IEventSource AddSignal(int signalNumber, Action<int> handler) =>
        throw new NotSupportedException("a window manager's loop has no signal sources");

    /// <inheritdoc/>
    public void AddIdle(Action handler) => loop.AddIdle(handler);

    /// <summary>
    /// Does nothing. The manager's loop runs idle callbacks before it waits.
    /// </summary>
    public void DispatchIdle()
    {
    }

    /// <summary>
    /// Disposes an object in an idle callback, in order with the other idle callbacks.
    /// </summary>
    /// <param name="victim">The object to dispose later.</param>
    /// <exception cref="ArgumentNullException"><paramref name="victim"/> is <see langword="null"/>.</exception>
    public void DeferDestroy(IDisposable victim)
    {
        ArgumentNullException.ThrowIfNull(victim);
        loop.AddIdle(victim.Dispose);
    }

    private sealed class Source(IWmEventSource inner) : IEventSource
    {
        public bool IsRemoved => inner.IsRemoved;

        public void Remove() => inner.Remove();

        public void UpdateTimer(int delayMs) => inner.UpdateTimer(delayMs);

        public void UpdateFd(FdReadiness events) => inner.UpdateFd((WmFdReadiness)events);
    }
}
