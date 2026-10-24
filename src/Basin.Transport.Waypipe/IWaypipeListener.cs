using System.Net;

namespace Basin.Transport.Waypipe;

/// <summary>
/// A listener that accepts a <see cref="WaypipeChannel"/> for each connection that waypipe opens. Disposal closes the listening socket and every live channel.
/// </summary>
public interface IWaypipeListener : IDisposable
{
    /// <summary>
    /// Gets the endpoint that the listener is bound to, with the actual port when the caller asked for any port.
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// Gets the number of accepted channels that have not ended.
    /// </summary>
    int Live { get; }

    /// <summary>
    /// Gets the number of channels that the listener accepted since it was bound.
    /// </summary>
    int Accepted { get; }

    /// <summary>
    /// Occurs on the compositor thread for each new channel. The listener owns the channel and disposes it when it ends.
    /// </summary>
    event Action<WaypipeChannel>? ChannelAccepted;

    /// <summary>
    /// Occurs on the compositor thread when the listener stops accepting because of an error.
    /// </summary>
    event Action<Exception>? Failed;
}
