using Basin.Capabilities;

using Basin.Diagnostics;

namespace PlasmaHost;

internal sealed class PlasmaHostFakeInputAuthority : IFakeInputAuthority
{
    public bool Authorize(in FakeInputRequest request)
    {
        BasinReport.Line($"FAKEINPUT application=\"{request.Application}\" reason=\"{request.Reason}\" pid={request.Pid}");
        return true;
    }

    public void Revoked(object client) => BasinReport.Line($"FAKEINPUT revoked");
}
