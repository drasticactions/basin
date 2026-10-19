namespace Basin.Capabilities;

public interface ISurfaceDiagnostics
{
    IDisposable WatchCommits(Func<Surface, bool> filter, Action<CommitTrace> sink);
}
