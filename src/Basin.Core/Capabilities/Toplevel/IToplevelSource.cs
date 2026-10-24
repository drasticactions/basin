namespace Basin.Capabilities;

/// <summary>
/// The toplevels of one shell, for example xdg-shell or XWayland, with ids local to the source.
/// <see cref="AggregateToplevelModel"/> combines sources into one <see cref="IToplevelModel"/>.
/// </summary>
public interface IToplevelSource
{
    /// <summary>
    /// Writes a snapshot of every toplevel of the source into a span, with local ids.
    /// </summary>
    /// <param name="toplevels">The destination. It must have space for every toplevel.</param>
    /// <returns>The number of entries written, or -1 when the span is too small.</returns>
    int Enumerate(Span<ToplevelInfo> toplevels);

    /// <summary>
    /// Gets a snapshot of one toplevel of the source.
    /// </summary>
    /// <param name="localId">The local id of the toplevel.</param>
    /// <param name="info">The snapshot, or the default value when the id is unknown.</param>
    /// <returns><see langword="true"/> when the toplevel exists.</returns>
    bool TryGet(ulong localId, out ToplevelInfo info);

    /// <summary>
    /// Asks the source to act on one of its toplevels. The handlers that policy installed on the source decide the result.
    /// </summary>
    /// <param name="localId">The local id of the toplevel.</param>
    /// <param name="request">The action and its arguments.</param>
    /// <returns><see langword="true"/> when the request was accepted. <see langword="false"/> when the id is unknown or the request was declined.</returns>
    bool Request(ulong localId, in ToplevelRequest request);

    /// <summary>
    /// Adds an observer for the add, change and remove notifications of the source, with local ids.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IToplevelObserver observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not present.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IToplevelObserver observer);

    /// <summary>
    /// Gets a value indicating whether the source sends the content commits of its toplevels to commit observers. The default is <see langword="false"/>.
    /// </summary>
    bool ReportsCommits => false;

    /// <summary>
    /// Adds an observer for the content commits of the toplevels of the source, with local ids. A source without commit reports ignores the call.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddCommitObserver(IToplevelCommitObserver observer)
    {
    }

    /// <summary>
    /// Removes a commit observer. A source without commit reports ignores the call.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveCommitObserver(IToplevelCommitObserver observer)
    {
    }
}
