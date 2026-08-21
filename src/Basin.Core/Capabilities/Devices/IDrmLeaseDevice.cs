namespace Basin.Capabilities;

public interface IDrmLeaseDevice
{
    int OpenEnumerationFd();

    int EnumerateConnectors(Span<LeasableConnector> connectors);

    bool TryCreateLease(ReadOnlySpan<uint> objectIds, out int leaseFd, out uint lesseeId);

    void RevokeLease(uint lesseeId);

    event Action<uint>? LeaseRevoked;

    event Action? ConnectorsChanged;
}
