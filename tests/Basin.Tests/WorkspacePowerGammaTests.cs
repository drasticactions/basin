using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class OutputPowerTests
{
    [Fact]
    public void Mode_round_trips_through_the_consumer()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        using var manager = new OutputPowerManager(host.Display, power);
        var requests = power.Requests;

        Basin.Desktop.Protocol.ZwlrOutputPowerManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_output_power_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrOutputPowerManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var control = proxy!.GetOutputPower(host.Client.Outputs[0]);
        var modes = new List<uint>();
        control.ModeEvent += (_, e) => modes.Add((uint)e.Mode);
        host.PumpUntil(() => modes.Count == 1);
        Assert.Equal(1u, modes[0]);

        control.SetMode(Basin.Desktop.Protocol.ZwlrOutputPowerV1.Mode.Off);
        host.PumpUntil(() => requests.Count == 1);
        Assert.Equal((host.Output, false), (requests[0].Output, requests[0].On));

        host.PumpUntil(() => modes.Count == 2);
        Assert.Equal(0u, modes[1]);

        control.Dispose();
        host.PumpToServer();
    }
}

public sealed class GammaControlTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Ramps_reach_the_consumer_and_reset_on_destroy()
    {
        using var host = new CompositorTestHost();
        var gamma = new TestOutputGamma();
        var applied = gamma.Applied;
        using var manager = new GammaControlManager(host.Display, gamma);

        Basin.Desktop.Protocol.ZwlrGammaControlManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_gamma_control_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrGammaControlManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var control = proxy!.GetGammaControl(host.Client.Outputs[0]);
        uint size = 0;
        var failed = false;
        control.GammaSize += (_, e) => size = e.Size;
        control.Failed += (_, _) => failed = true;
        host.PumpUntil(() => size != 0 || failed);
        Assert.Equal(4u, size);

        var table = new ushort[12];
        for (var i = 0; i < 4; i++)
        {
            table[i] = (ushort)(0x1000 + i);
            table[4 + i] = (ushort)(0x2000 + i);
            table[8 + i] = (ushort)(0x3000 + i);
        }

        var fd = memfd_create("gamma", 0);
        unsafe
        {
            fixed (ushort* data = table)
            {
                Assert.Equal(24, (int)write(fd, (byte*)data, 24));
            }
        }

        lseek(fd, 0, 0);
        control.SetGamma(fd);
        close(fd);
        host.PumpUntil(() => applied.Count == 1);
        Assert.NotNull(applied[0]);
        Assert.Equal(0x1003, applied[0]!.Value.Red[3]);
        Assert.Equal(0x2000, applied[0]!.Value.Green[0]);
        Assert.Equal(0x3002, applied[0]!.Value.Blue[2]);

        control.Dispose();
        host.PumpUntil(() => applied.Count == 2);
        Assert.Null(applied[1]);

        host.PumpToServer();
    }
}

public sealed class WorkspaceTests
{
    [Fact]
    public void Workspaces_announce_and_requests_wait_for_commit()
    {
        using var host = new CompositorTestHost();
        using var manager = new WorkspaceManager(host.Display);

        var group = manager.CreateGroup(clientsCanCreateWorkspaces: true);
        group.AddOutput(host.OutputGlobals()[0].Global);
        var main = group.CreateWorkspace("main", id: "ws-main", state: WorkspaceManager.WorkspaceState.Active);

        Basin.Desktop.Protocol.ExtWorkspaceManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_workspace_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtWorkspaceManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        Basin.Desktop.Protocol.ExtWorkspaceGroupHandleV1? groupHandle = null;
        Basin.Desktop.Protocol.ExtWorkspaceHandleV1? wsHandle = null;
        var names = new List<string>();
        var states = new List<uint>();
        var groupCaps = 0u;
        var entered = 0;
        var done = 0;
        proxy!.WorkspaceGroup += (_, e) =>
        {
            groupHandle = e.WorkspaceGroup;
            groupHandle.Capabilities += (_, ce) => groupCaps = (uint)ce.Capabilities;
            groupHandle.WorkspaceEnter += (_, _) => entered++;
        };
        proxy.Workspace += (_, e) =>
        {
            wsHandle = e.Workspace;
            wsHandle.Name += (_, ne) => names.Add(ne.Name);
            wsHandle.StateEvent += (_, se) => states.Add((uint)se.State);
        };
        proxy.Done += (_, _) => done++;
        host.PumpUntil(() => done >= 1);

        Assert.NotNull(groupHandle);
        Assert.NotNull(wsHandle);
        Assert.Equal(1u, groupCaps);
        Assert.Equal("main", Assert.Single(names));
        Assert.Equal(1u, Assert.Single(states));
        Assert.Equal(1, entered);

        var activated = 0;
        main.ActivateRequested += () => activated++;
        var created = new List<string>();
        group.CreateWorkspaceRequested += name => created.Add(name);

        wsHandle!.Activate();
        groupHandle!.CreateWorkspace("scratch");
        host.PumpToServer();
        Assert.Equal(0, activated);
        Assert.Empty(created);

        proxy.Commit();
        host.PumpUntil(() => activated == 1 && created.Count == 1);
        Assert.Equal("scratch", created[0]);

        main.SetState(WorkspaceManager.WorkspaceState.Active | WorkspaceManager.WorkspaceState.Urgent);
        host.PumpUntil(() => states.Count == 2);
        Assert.Equal(3u, states[1]);

        var removed = false;
        wsHandle.Removed += (_, _) => removed = true;
        main.Remove();
        host.PumpUntil(() => removed);

        host.PumpToServer();
    }
}
