using Tmds.DBus.Protocol;

namespace Basin.Portal;

/// <summary>
/// A <c>org.freedesktop.impl.portal.Request</c> object for one pending method call. The frontend's <c>Close</c> cancels <see cref="Token"/>.
/// </summary>
public sealed class PortalRequest : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    internal PortalRequest(ObjectPath handle) => Handle = handle;

    /// <summary>
    /// Gets the request object path that the frontend gave.
    /// </summary>
    public ObjectPath Handle { get; }

    /// <summary>
    /// Gets the token that is canceled when the request closes. A handler passes it to the prompt it waits on.
    /// </summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Gets a value indicating whether the frontend or the bus closed the request.
    /// </summary>
    public bool IsClosed => _cancellation.IsCancellationRequested;

    /// <summary>
    /// Cancels <see cref="Token"/>. It does not remove the request from the bus.
    /// </summary>
    public void Close() => _cancellation.Cancel();

    /// <summary>
    /// Releases the cancellation source. <see cref="PortalBus.Release"/> calls it.
    /// </summary>
    public void Dispose() => _cancellation.Dispose();
}
