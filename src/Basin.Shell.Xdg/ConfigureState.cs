using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The progress of the configure that an <see cref="XdgSurfaceState"/> sent with a <see cref="Transaction"/>. A configure sent without a transaction does not change it.
/// </summary>
public enum ConfigureState
{
    /// <summary>
    /// No configure with a transaction was sent yet.
    /// </summary>
    Idle,

    /// <summary>
    /// The configure was sent and the client did not acknowledge it yet.
    /// </summary>
    Inflight,

    /// <summary>
    /// The client acknowledged the configure but did not commit a buffer yet.
    /// </summary>
    Acked,

    /// <summary>
    /// The client acknowledged the configure and committed a buffer, so the surface is ready for the transaction.
    /// </summary>
    Committed,

    /// <summary>
    /// The transaction completed before the client acknowledged the configure and committed a buffer.
    /// </summary>
    TimedOut,
}
