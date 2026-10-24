using Basin.Eis;

namespace Basin.Portal;

/// <summary>
/// The capture state of one portal session: its barriers, its enabled state and its EIS sender. Disposing it removes the barriers and ends the capture.
/// </summary>
public interface IInputCaptureHandle : IDisposable
{
    /// <summary>
    /// Gets the id of the last activation, or 0 before the first one.
    /// </summary>
    uint ActivationId { get; }

    /// <summary>
    /// Removes every barrier of the session.
    /// </summary>
    void ClearBarriers();

    /// <summary>
    /// Adds a barrier on a zone edge.
    /// </summary>
    /// <param name="barrier">The barrier to add.</param>
    /// <returns><see langword="false"/> when the id exists or the barrier is not on a zone edge.</returns>
    bool TryAddBarrier(in InputCaptureBarrier barrier);

    /// <summary>
    /// Makes the session wait for a barrier crossing. It does nothing while the session is activated.
    /// </summary>
    void Enable();

    /// <summary>
    /// Ends an activation when there is one, and stops the session from waiting for a crossing.
    /// </summary>
    void Disable();

    /// <summary>
    /// Ends an activation and gives the input back to the compositor.
    /// </summary>
    /// <param name="activationId">The activation that the client releases.</param>
    /// <param name="x">The layout x to move the pointer to, or -1 for no move.</param>
    /// <param name="y">The layout y to move the pointer to, or -1 for no move.</param>
    /// <returns><see langword="false"/> when the session is activated and <paramref name="activationId"/> is not the current activation.</returns>
    bool Release(uint activationId, double x, double y);

    /// <summary>
    /// Creates a new EIS client connection to the session's sender.
    /// </summary>
    /// <returns>The client end of the socket, which the caller owns, or a negative value on failure.</returns>
    int TakeEisFd();
}
