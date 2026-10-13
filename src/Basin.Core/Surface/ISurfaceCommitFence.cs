namespace Basin;

public interface ISurfaceCommitFence : IDisposable
{
    bool Signaled { get; }

    event Action<ISurfaceCommitFence>? SignaledChanged;
}
