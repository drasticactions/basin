namespace Basin.Capabilities;

/// <summary>
/// The stacking order of toplevels, as ids of the <see cref="IToplevelModel"/>.
/// </summary>
public interface IToplevelStack
{
    /// <summary>
    /// Writes the toplevel ids in stacking order, bottom first.
    /// </summary>
    /// <param name="toplevels">The destination. It must have space for every toplevel.</param>
    /// <returns>The number of ids written, or -1 when the span is too small.</returns>
    int Enumerate(Span<ulong> toplevels);

    /// <summary>
    /// Adds an observer for changes of the stacking order.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IToplevelStackObserver observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not present.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IToplevelStackObserver observer);

    /// <summary>
    /// Finds the topmost toplevel at a point. The default implementation cannot answer and returns <see langword="false"/>.
    /// </summary>
    /// <param name="x">The x coordinate in layout coordinates.</param>
    /// <param name="y">The y coordinate in layout coordinates.</param>
    /// <param name="toplevelId">The id of the topmost toplevel at the point, or 0 when no toplevel is there.</param>
    /// <returns><see langword="true"/> when the stack can answer, also when no toplevel is at the point. <see langword="false"/> tells the caller to do its own hit test.</returns>
    bool TryToplevelAt(double x, double y, out ulong toplevelId)
    {
        toplevelId = 0;
        return false;
    }
}
