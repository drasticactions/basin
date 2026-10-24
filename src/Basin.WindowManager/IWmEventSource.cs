namespace Basin.WindowManager;

/// <summary>
/// An fd watch or a timer on an <see cref="IWmEventLoop"/>.
/// </summary>
public interface IWmEventSource
{
    /// <summary>
    /// Gets a value indicating whether <see cref="Remove"/> ran.
    /// </summary>
    bool IsRemoved { get; }

    /// <summary>
    /// Stops the source. Its callback does not run again. A second call does nothing.
    /// </summary>
    void Remove();

    /// <summary>
    /// Arms the timer to fire once after a delay, and replaces an earlier deadline. It has no effect on a removed source.
    /// </summary>
    /// <param name="delayMs">The delay in milliseconds. Zero or less disarms the timer.</param>
    void UpdateTimer(int delayMs);

    /// <summary>
    /// Changes the readiness that an fd watch waits for.
    /// </summary>
    /// <param name="events">The new readiness to wait for.</param>
    void UpdateFd(WmFdReadiness events);
}
