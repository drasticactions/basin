using System.Buffers;

namespace Basin.Capabilities;

/// <summary>
/// The toplevel windows of a session, as protocols and IPC see them.
/// An id stays the same for the life of its window, and 0 is never a valid id.
/// </summary>
public interface IToplevelModel
{
    /// <summary>
    /// Writes a snapshot of every toplevel into a span.
    /// </summary>
    /// <param name="toplevels">The destination. It must have space for every toplevel.</param>
    /// <returns>The number of entries written, or -1 when the span is too small. The span content is then undefined.</returns>
    int Enumerate(Span<ToplevelInfo> toplevels);

    /// <summary>
    /// Gets a snapshot of one toplevel.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    /// <param name="info">The snapshot, or the default value when the id is unknown.</param>
    /// <returns><see langword="true"/> when the toplevel exists.</returns>
    bool TryGet(ulong toplevelId, out ToplevelInfo info);

    /// <summary>
    /// Adds an observer for the add, change and remove notifications of toplevels.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IToplevelObserver observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not present.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IToplevelObserver observer);

    /// <summary>
    /// Asks the owner of a toplevel to act on it, for example to activate or close it. Policy decides the result.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    /// <param name="request">The action and its arguments.</param>
    /// <returns><see langword="true"/> when the owner accepted the request. <see langword="false"/> when the id is unknown or the owner declined.</returns>
    bool Request(ulong toplevelId, in ToplevelRequest request);

    /// <summary>
    /// Finds the toplevel whose root surface is <paramref name="surface"/>.
    /// The default implementation enumerates every toplevel through a pooled buffer.
    /// </summary>
    /// <param name="surface">The root surface to look for.</param>
    /// <param name="info">The snapshot, or the default value when no toplevel has the surface.</param>
    /// <returns><see langword="true"/> when a toplevel has the surface.</returns>
    bool TryFind(Surface surface, out ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var length = 64;
        while (true)
        {
            var buffer = ArrayPool<ToplevelInfo>.Shared.Rent(length);
            var count = Enumerate(buffer);
            if (count < 0)
            {
                length = buffer.Length * 2;
                ArrayPool<ToplevelInfo>.Shared.Return(buffer, clearArray: true);
                continue;
            }

            info = default;
            var found = false;
            for (var i = 0; i < count; i++)
            {
                if (ReferenceEquals(buffer[i].Surface, surface))
                {
                    info = buffer[i];
                    found = true;
                    break;
                }
            }

            ArrayPool<ToplevelInfo>.Shared.Return(buffer, clearArray: true);
            return found;
        }
    }

    /// <summary>
    /// Finds whether commit observers receive the content commits of a toplevel. The default implementation returns <see langword="false"/>.
    /// </summary>
    /// <param name="toplevelId">The toplevel id.</param>
    /// <returns><see langword="true"/> when the toplevel exists and its commits go to commit observers.</returns>
    bool ReportsCommits(ulong toplevelId) => false;

    /// <summary>
    /// Adds an observer for the content commits of toplevels. A model without commit reports ignores the call.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddCommitObserver(IToplevelCommitObserver observer)
    {
    }

    /// <summary>
    /// Removes a commit observer. A model without commit reports ignores the call.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveCommitObserver(IToplevelCommitObserver observer)
    {
    }
}
