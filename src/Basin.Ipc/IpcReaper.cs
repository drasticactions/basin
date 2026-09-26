namespace Basin.Ipc;

internal sealed class IpcReaper(int pid, int pidfd) : IDisposable
{
    private bool _disposed;

    public int Pid { get; } = pid;

    public IEventSource? Source { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Source?.Remove();
        Source = null;
        _ = UnixSocket.Close(pidfd);
    }
}
