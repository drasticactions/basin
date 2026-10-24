namespace Basin;

/// <summary>
/// A file descriptor, timer or signal registration on an <see cref="ICompositorEventLoop"/>.
/// </summary>
public interface IEventSource
{
    /// <summary>
    /// Gets a value indicating whether <see cref="Remove"/> was called. A removed source never fires again.
    /// </summary>
    bool IsRemoved { get; }

    /// <summary>
    /// Stops the source and releases the descriptors that the loop made for it. A descriptor from the caller stays open. A second call does nothing.
    /// </summary>
    void Remove();

    /// <summary>
    /// Arms a timer source to fire once after a delay. A delay of 0 or less disarms it. A source that is not a timer throws <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="delayMs">The delay in milliseconds.</param>
    void UpdateTimer(int delayMs);

    /// <summary>
    /// Changes the conditions that a descriptor source watches. A timer or signal source throws <see cref="InvalidOperationException"/> or ignores the call.
    /// </summary>
    /// <param name="events">The new watch mask.</param>
    void UpdateFd(FdReadiness events);
}
