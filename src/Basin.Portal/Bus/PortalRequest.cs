using Tmds.DBus.Protocol;

namespace Basin.Portal;

public sealed class PortalRequest : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    internal PortalRequest(ObjectPath handle) => Handle = handle;

    public ObjectPath Handle { get; }

    public CancellationToken Token => _cancellation.Token;

    public bool IsClosed => _cancellation.IsCancellationRequested;

    public void Close() => _cancellation.Cancel();

    public void Dispose() => _cancellation.Dispose();
}
