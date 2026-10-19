namespace Basin.Capabilities;

public interface IClientDirectory
{
    bool CanListObjects { get; }

    int Enumerate(Span<ClientInfo> clients);

    int Objects(ulong clientId, Span<ClientObjectInfo> objects);
}
