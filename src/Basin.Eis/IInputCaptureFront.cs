namespace Basin.Eis;

/// <summary>
/// Receives the state changes of one <see cref="InputCaptureSession"/>. A protocol or portal front implements it and sends each change to its client.
/// </summary>
public interface IInputCaptureFront
{
    /// <summary>
    /// Called when the pointer crosses a barrier and the session takes the input.
    /// </summary>
    /// <param name="activationId">The id of this activation. It increases for each activation of the session.</param>
    /// <param name="x">The layout x of the pointer at activation.</param>
    /// <param name="y">The layout y of the pointer at activation.</param>
    /// <param name="barrierId">The id of the barrier that the pointer crossed.</param>
    void Activated(uint activationId, double x, double y, uint barrierId);

    /// <summary>
    /// Called when the session gives the input back to the compositor.
    /// </summary>
    /// <param name="activationId">The id of the activation that ended.</param>
    void Deactivated(uint activationId);

    /// <summary>
    /// Called when the session goes from enabled back to created, for example after a layout change or a forced release.
    /// </summary>
    void Disabled();

    /// <summary>
    /// Called when the output layout changes. The session has dropped its barriers and is disabled, so the client must set new ones.
    /// </summary>
    void ZonesChanged();
}
