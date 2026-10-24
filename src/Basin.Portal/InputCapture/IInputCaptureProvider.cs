using Basin.Eis;

namespace Basin.Portal;

/// <summary>
/// The source of input capture for <see cref="PortalInputCaptureModule"/>. The compositor or a Wayland client of a capture protocol implements it.
/// </summary>
public interface IInputCaptureProvider
{
    /// <summary>
    /// Gets the zone set serial. It increases on each layout change, so a barrier request with an old serial fails.
    /// </summary>
    uint ZoneSet { get; }

    /// <summary>
    /// Starts capture state for a portal session.
    /// </summary>
    /// <param name="front">The portal session that receives activations and zone changes.</param>
    /// <param name="handle">The session id for log lines.</param>
    /// <returns>The capture handle that the caller disposes, or <see langword="null"/> when capture is not available.</returns>
    IInputCaptureHandle? Open(IInputCaptureFront front, string handle);
}
