namespace Basin.Capabilities;

public interface IFakeInputAuthority
{
    bool Authorize(in FakeInputRequest request);

    void Revoked(object client)
    {
    }
}
