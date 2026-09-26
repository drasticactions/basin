using Basin.Ipc;
using Basin.Scene;
using Basin.Shell.Nested;
using Basin.Tests.Nested;

namespace Basin.Tests;

internal sealed class IpcHostedRig : IDisposable
{
    private readonly List<IpcTestPeer> _peers = [];

    public IpcHostedRig(ShellSettings? settings = null, Action<IpcServer>? register = null)
    {
        SceneCapturePack? pack = null;
        Harness = new NestedShellHarness(
            settings: settings,
            configure: (host, services) => services.With(pack = new SceneCapturePack(host.Scene, host.Layout)));
        Capture = pack!;
        Harness.Shell.AttachCapture(Capture);
        Server = new IpcServer(Harness.Host.Loop, Harness.Host.Services, new IpcSessionInfo { Compositor = "hosted" }, null, listen: false)
        {
            SyntheticInput = new NestedSyntheticInput(Harness.Shell),
        };
        register?.Invoke(Server);
        Server.Start();
    }

    public NestedShellHarness Harness { get; }

    public SceneCapturePack Capture { get; }

    public IpcServer Server { get; }

    public IpcTestPeer Connect()
    {
        Xunit.Assert.Equal(0, UnixSocket.Pair(out var mine, out var theirs));
        Server.Adopt(theirs);
        var peer = new IpcTestPeer(mine, Harness.Pump);
        _peers.Add(peer);
        return peer;
    }

    public void Dispose()
    {
        foreach (var peer in _peers)
        {
            peer.Dispose();
        }

        Server.Dispose();
        Harness.Dispose();
    }
}
