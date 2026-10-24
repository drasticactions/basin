namespace Basin.Capabilities;

/// <summary>
/// A connector that a client can lease, with the KMS objects that a lease of it must include.
/// </summary>
/// <param name="Name">The connector name, for example <c>DP-2</c>.</param>
/// <param name="Description">A readable description, for example the make and model from the EDID.</param>
/// <param name="ConnectorId">The KMS connector id.</param>
/// <param name="ObjectIds">The connector, CRTC and plane ids to pass to <see cref="IDrmLeaseDevice.TryCreateLease"/>.</param>
public readonly record struct LeasableConnector(
    string Name,
    string Description,
    uint ConnectorId,
    uint[] ObjectIds);
