using Basin.Capabilities;
using Basin.Desktop;
using Basin.Plasma;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DpmsTests
{
    private sealed class DpmsView
    {
        public Basin.Plasma.Protocol.OrgKdeKwinDpms Proxy = null!;
        public readonly List<string> Order = [];
        public readonly List<uint> Modes = [];
        public uint? Supported;
        public int DoneCount;
    }

    private static Basin.Plasma.Protocol.OrgKdeKwinDpmsManager BindManager(CompositorTestHost host, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinDpmsManager? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_dpms_manager")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinDpmsManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static DpmsView GetDpms(Basin.Plasma.Protocol.OrgKdeKwinDpmsManager manager, WlOutput output)
    {
        var view = new DpmsView { Proxy = manager.Get(output) };
        view.Proxy.Supported += (_, e) =>
        {
            view.Order.Add("supported");
            view.Supported = e.Supported;
        };
        view.Proxy.ModeEvent += (_, e) =>
        {
            view.Order.Add("mode");
            view.Modes.Add(e.Mode);
        };
        view.Proxy.Done += (_, _) =>
        {
            view.Order.Add("done");
            view.DoneCount++;
        };
        return view;
    }

    [Fact]
    public void A_bind_pushes_supported_then_mode_then_done()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        using var manager = new DpmsManager(host.Display, power);

        var proxy = BindManager(host);
        var view = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal(["supported", "mode", "done"], view.Order);
        Assert.Equal(1u, view.Supported);
        Assert.Equal([0u], view.Modes);
    }

    [Fact]
    public void Supported_is_false_without_a_capability_and_mode_is_still_on()
    {
        using var host = new CompositorTestHost();
        using var manager = new DpmsManager(host.Display, power: null);

        var proxy = BindManager(host);
        var view = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal(0u, view.Supported);
        Assert.Equal([0u], view.Modes);
    }

    [Fact]
    public void Standby_and_suspend_both_turn_the_output_off_and_report_off()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        var requests = power.Requests;
        using var manager = new DpmsManager(host.Display, power);

        var proxy = BindManager(host);
        var view = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => view.DoneCount >= 1);

        view.Proxy.Set((uint)Basin.Plasma.Protocol.OrgKdeKwinDpms.Mode.Standby);
        host.PumpUntil(() => view.Modes.Count >= 2);
        Assert.Equal((host.Output, false), (requests[0].Output, requests[0].On));
        Assert.Equal(3u, view.Modes[1]);

        view.Proxy.Set((uint)Basin.Plasma.Protocol.OrgKdeKwinDpms.Mode.Suspend);
        host.PumpUntil(() => view.Modes.Count >= 3);
        Assert.Equal((host.Output, false), (requests[1].Output, requests[1].On));
        Assert.Equal(3u, view.Modes[2]);
        Assert.Equal(3, view.DoneCount);
    }

    [Fact]
    public void A_change_made_elsewhere_pushes_mode_and_done_here()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        using var wlr = new OutputPowerManager(host.Display, power);
        using var manager = new DpmsManager(host.Display, power);

        var proxy = BindManager(host);
        var view = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => view.DoneCount >= 1);

        Basin.Desktop.Protocol.ZwlrOutputPowerManagerV1? wlrProxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_output_power_manager_v1")
            {
                wlrProxy = registry.Bind<Basin.Desktop.Protocol.ZwlrOutputPowerManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var control = wlrProxy!.GetOutputPower(host.Client.Outputs[0]);
        control.SetMode(Basin.Desktop.Protocol.ZwlrOutputPowerV1.Mode.Off);
        host.PumpUntil(() => view.Modes.Count >= 2);
        Assert.Equal(3u, view.Modes[1]);
        Assert.Equal(2, view.DoneCount);

        power.SetOn(host.Output, true);
        host.PumpUntil(() => view.Modes.Count >= 3);
        Assert.Equal(0u, view.Modes[2]);
        Assert.Equal(3, view.DoneCount);
    }

    [Fact]
    public void Two_dpms_objects_for_one_output_both_see_every_change()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        using var manager = new DpmsManager(host.Display, power);

        var proxy = BindManager(host);
        var first = GetDpms(proxy, host.Client.Outputs[0]);
        var second = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => first.DoneCount >= 1 && second.DoneCount >= 1);

        power.SetOn(host.Output, false);
        host.PumpUntil(() => first.DoneCount >= 2 && second.DoneCount >= 2);
        Assert.Equal(3u, first.Modes[^1]);
        Assert.Equal(3u, second.Modes[^1]);
    }

    [Fact]
    public void An_unplugged_outputs_object_stops_updating_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        var extra = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        var extraGlobal = new OutputGlobal(host.Display, extra);
        host.Layout.Add(extra, 320, 0);
        var power = new TestOutputPower();
        using var manager = new DpmsManager(host.Display, power);

        var client = host.ConnectClient();
        var proxy = BindManager(host, client);
        var view = GetDpms(proxy, client.Outputs[1]);
        host.PumpUntil(() => view.DoneCount >= 1);
        Assert.Equal(1u, view.Supported);

        host.Layout.Remove(extra);
        extraGlobal.Dispose();
        extra.Destroy();
        host.PumpToClient();

        power.SetOn(extra, false);
        host.PumpToClient();
        Assert.Equal(1, view.DoneCount);
        Assert.Equal([0u], view.Modes);
        AssertClientAlive(host, client);
    }

    [Fact]
    public void Release_destroys_cleanly_with_the_output_still_present()
    {
        using var host = new CompositorTestHost();
        var power = new TestOutputPower();
        using var manager = new DpmsManager(host.Display, power);

        var proxy = BindManager(host);
        var view = GetDpms(proxy, host.Client.Outputs[0]);
        host.PumpUntil(() => view.DoneCount >= 1);

        view.Proxy.Release();
        host.PumpToServer();

        power.SetOn(host.Output, false);
        host.PumpToClient();
        Assert.Equal(1, view.DoneCount);
        AssertClientAlive(host, host.Client);
    }

    private static void AssertClientAlive(CompositorTestHost host, ShmTestClient client)
    {
        host.PumpToServer();
        host.PumpToClient();
        var sync = client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }
}
