using Basin.Capabilities;

namespace PlasmaHost;

internal sealed class PlasmaHostFakeInputAuthority : IFakeInputAuthority
{
    public bool Authorize(in FakeInputRequest request)
    {
        Console.WriteLine($"FAKEINPUT application=\"{request.Application}\" reason=\"{request.Reason}\" pid={request.Pid}");
        return true;
    }

    public void Revoked(object client) => Console.WriteLine("FAKEINPUT revoked");
}
