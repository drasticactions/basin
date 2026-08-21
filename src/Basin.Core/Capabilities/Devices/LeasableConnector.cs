namespace Basin.Capabilities;

public readonly record struct LeasableConnector(
    string Name,
    string Description,
    uint ConnectorId,
    uint[] ObjectIds);
