using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcCallAllocationTests
{
    private const int Rounds = 50;

    public static TheoryData<string, string> Calls => new()
    {
        { "ipc-call-version", """{"method":"ipc/version"}""" },
        { "ipc-call-methods", """{"method":"ipc/methods"}""" },
        { "ipc-call-methods-detail", """{"method":"ipc/methods","params":{"detail":true}}""" },
        { "ipc-call-events", """{"method":"ipc/events"}""" },
        { "ipc-call-session-describe", """{"method":"session/describe"}""" },
        { "ipc-call-outputs-list", """{"method":"outputs/list"}""" },
        { "ipc-call-outputs-test", """{"method":"outputs/test","params":{"entries":[{"name":"HEADLESS-1","scale":1}]}}""" },
        { "ipc-call-windows-list", """{"method":"windows/list"}""" },
        { "ipc-call-windows-get", """{"id":7,"method":"windows/get","params":{"id":2}}""" },
        { "ipc-call-windows-get-missing", """{"id":7,"method":"windows/get","params":{"id":999}}""" },
        { "ipc-call-windows-stack", """{"method":"windows/stack"}""" },
        { "ipc-call-windows-move", """{"method":"windows/move","params":{"id":2,"x":10,"y":20}}""" },
        { "ipc-call-windows-set-state", """{"method":"windows/set-state","params":{"id":2,"maximized":true}}""" },
        { "ipc-call-workspaces-list", """{"method":"workspaces/list"}""" },
        { "ipc-call-idle-status", """{"method":"idle/status"}""" },
        { "ipc-call-lock-status", """{"method":"lock/status"}""" },
        { "ipc-call-keyboard-keymap", """{"method":"keyboard/keymap"}""" },
        { "ipc-call-input-key", """{"method":"input/key","params":{"code":30}}""" },
        { "ipc-call-input-chord", """{"method":"input/chord","params":{"chord":"Super+Shift+c"}}""" },
        { "ipc-call-input-text", """{"method":"input/text","params":{"text":"aA"}}""" },
        { "ipc-call-seat-pointer-move", """{"method":"seat/pointer-move","params":{"x":10.5,"y":20}}""" },
        { "ipc-call-clipboard-empty", """{"method":"clipboard/read"}""" },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void A_control_call_stays_within_budget(string path, string request)
    {
        Budgets.Require();
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        _ = model.Add("one", "app.one", geometry: new Box(0, 0, 100, 80));
        _ = model.Add("two", "app.two", geometry: new Box(20, 10, 60, 40));
        _ = model.Add("three", "app.three", geometry: new Box(40, 30, 50, 50));
        ((TestToplevelStack)rig.Services.Require<IToplevelStack>()).SetOrder(1, 2, 3);
        var workspaces = (TestWorkspaceModel)rig.Services.Require<IWorkspaceModel>();
        var group = workspaces.AddGroup(true, rig.Host.Output);
        _ = workspaces.AddWorkspace(group, "one", state: WorkspaceStateFlags.Active);
        _ = workspaces.AddWorkspace(group, "two");
        var peer = rig.Connect();
        var frame = IpcTestPeer.Frame(request);

        long measured = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            measured = 0;
            for (var round = 0; round < Rounds; round++)
            {
                peer.SendRaw(frame);
                var before = GC.GetAllocatedBytesForCurrentThread();
                rig.Host.Loop.Dispatch(0);
                measured += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.StartsWith("{", peer.Receive(), StringComparison.Ordinal);
            }
        }

        Assert.True(measured > 0, "the call was not handled inside the measured dispatch");
        Budgets.Check("server", path, measured);
    }

    [Fact]
    public void A_call_through_an_interceptor_stays_within_budget()
    {
        Budgets.Require();
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = new PassThrough());
        var peer = rig.Connect();
        var frame = IpcTestPeer.Frame("""{"method":"ipc/version"}""");

        long measured = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            measured = 0;
            for (var round = 0; round < Rounds; round++)
            {
                peer.SendRaw(frame);
                var before = GC.GetAllocatedBytesForCurrentThread();
                rig.Host.Loop.Dispatch(0);
                measured += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.StartsWith("{", peer.Receive(), StringComparison.Ordinal);
            }
        }

        Assert.True(measured > 0, "the call was not handled inside the measured dispatch");
        Budgets.Check("server", "ipc-call-version-intercepted", measured);
    }

    private sealed class PassThrough : IIpcInterceptor
    {
        public IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context) => IpcDecision.Allow;

        public void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
        {
        }
    }
}
