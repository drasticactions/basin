namespace Basin;

/// <summary>
/// A fence that a parked commit waits for. The surface disposes it after it signals or when the surface is destroyed.
/// </summary>
public interface ISurfaceCommitFence : IDisposable
{
    /// <summary>Gets a value indicating whether the fence has signaled.</summary>
    bool Signaled { get; }

    /// <summary>Occurs on the compositor thread when the fence signals, with the fence as the argument.</summary>
    event Action<ISurfaceCommitFence>? SignaledChanged;
}
