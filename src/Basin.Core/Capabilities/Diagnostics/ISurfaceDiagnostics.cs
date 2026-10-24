namespace Basin.Capabilities;

/// <summary>
/// Reports surface commits and frame callbacks for diagnostics.
/// </summary>
public interface ISurfaceDiagnostics
{
    /// <summary>
    /// Starts to send a trace for each commit and each frame-done of the surfaces that <paramref name="filter"/> accepts.
    /// </summary>
    /// <param name="filter">Returns <see langword="true"/> for each surface to trace.</param>
    /// <param name="sink">The callback for each trace. It runs on the compositor thread.</param>
    /// <returns>A handle that stops the watch when disposed.</returns>
    IDisposable WatchCommits(Func<Surface, bool> filter, Action<CommitTrace> sink);
}
