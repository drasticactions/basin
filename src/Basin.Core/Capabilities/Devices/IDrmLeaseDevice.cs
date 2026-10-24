namespace Basin.Capabilities;

/// <summary>
/// A DRM device that can lease connectors to clients, for example a VR headset to its runtime.
/// </summary>
public interface IDrmLeaseDevice
{
    /// <summary>
    /// Opens a new file descriptor on the device that is not DRM master, for a client to enumerate the card.
    /// </summary>
    /// <returns>A new fd that the caller owns and must close, or -1 when the device cannot give one.</returns>
    int OpenEnumerationFd();

    /// <summary>
    /// Copies the connectors that can be leased into a caller buffer.
    /// </summary>
    /// <param name="connectors">The buffer to fill.</param>
    /// <returns>The number of connectors written, or -1 when the buffer is too small.</returns>
    int EnumerateConnectors(Span<LeasableConnector> connectors);

    /// <summary>
    /// Creates a lease of the given KMS objects.
    /// </summary>
    /// <param name="objectIds">The connector, CRTC and plane ids to lease, as <see cref="LeasableConnector.ObjectIds"/> lists them.</param>
    /// <param name="leaseFd">The lease fd for the lessee, or -1 on failure. The caller owns it.</param>
    /// <param name="lesseeId">The kernel's lessee id, or 0 on failure.</param>
    /// <returns><see langword="false"/> when the list is empty, the session is not active or the kernel refuses the lease.</returns>
    bool TryCreateLease(ReadOnlySpan<uint> objectIds, out int leaseFd, out uint lesseeId);

    /// <summary>
    /// Revokes a lease that <see cref="TryCreateLease"/> created. An unknown lessee id is ignored, and this call
    /// does not raise <see cref="LeaseRevoked"/>.
    /// </summary>
    /// <param name="lesseeId">The lessee id of the lease.</param>
    void RevokeLease(uint lesseeId);

    /// <summary>
    /// Occurs when the device revokes a lease by itself, for example because a leased connector went away.
    /// The argument is the lessee id.
    /// </summary>
    event Action<uint>? LeaseRevoked;

    /// <summary>
    /// Occurs when the set of leasable connectors changes.
    /// </summary>
    event Action? ConnectorsChanged;
}
