using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Plasma;
using Xunit;

namespace Basin.Tests;

public sealed class ExternalBrightnessTests
{
    private static byte[] Edid(byte seed)
    {
        var edid = new byte[128];
        for (var i = 0; i < edid.Length; i++)
        {
            edid[i] = (byte)(seed + i);
        }

        return edid;
    }

    private static Basin.Plasma.Protocol.KdeExternalBrightnessV1 Bind(CompositorTestHost host)
    {
        Basin.Plasma.Protocol.KdeExternalBrightnessV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_external_brightness_v1")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.KdeExternalBrightnessV1>(e.Name, 3);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static Basin.Plasma.Protocol.KdeExternalBrightnessDeviceV1 Register(
        CompositorTestHost host,
        Basin.Plasma.Protocol.KdeExternalBrightnessV1 proxy,
        byte[]? edid,
        uint max = 100,
        bool internalPanel = false,
        bool ddcCi = false,
        uint? observed = null)
    {
        var device = proxy.CreateBrightnessControl();
        device.SetInternal(internalPanel ? 1u : 0u);
        device.SetEdid(edid is null ? string.Empty : Convert.ToBase64String(edid));
        device.SetMaxBrightness(max);
        if (observed is { } value)
        {
            device.SetObservedBrightness(value);
        }

        if (ddcCi)
        {
            device.SetUsesDdcCi(1);
        }

        device.Commit();
        host.PumpToServer();
        return device;
    }

    [Fact]
    public void A_matching_edid_serves_that_output()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        Register(host, proxy, edid);

        Assert.True(manager.Control.Supports(output));
        Assert.Equal(100u, manager.Control.Max(output));
        output.Destroy();
    }

    [Fact]
    public void An_unmatched_edid_serves_nothing_silently()
    {
        using var host = new CompositorTestHost();
        var output = new TestEdidOutput("DP-3", Edid(7));
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, Edid(9));
        var requested = false;
        device.RequestedBrightness += (_, _) => requested = true;

        Assert.False(manager.Control.Supports(output));
        Assert.False(manager.Control.Set(output, 50));
        host.PumpToClient();
        Assert.False(requested);
        output.Destroy();
    }

    [Fact]
    public void Two_outputs_with_the_same_edid_serve_neither()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var first = new TestEdidOutput("DP-1", edid);
        var second = new TestEdidOutput("DP-2", edid);
        var layout = new OutputLayout();
        layout.Add(first, 0, 0);
        layout.Add(second, 640, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        Register(host, proxy, edid);

        Assert.False(manager.Control.Supports(first));
        Assert.False(manager.Control.Supports(second));
        first.Destroy();
        second.Destroy();
    }

    [Fact]
    public void An_internal_device_with_no_edid_falls_back_to_the_internal_connector()
    {
        using var host = new CompositorTestHost();
        var external = new TestEdidOutput("DP-1", Edid(7));
        var panel = new TestEdidOutput("eDP-1", []);
        var layout = new OutputLayout();
        layout.Add(external, 0, 0);
        layout.Add(panel, 640, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        Register(host, proxy, edid: null, internalPanel: true);

        Assert.True(manager.Control.Supports(panel));
        Assert.False(manager.Control.Supports(external));
        external.Destroy();
        panel.Destroy();
    }

    [Fact]
    public void Set_sends_one_requested_brightness_with_the_value()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, edid);
        var values = new List<uint>();
        device.RequestedBrightness += (_, e) => values.Add(e.Value);

        Assert.True(manager.Control.Set(output, 42));
        host.PumpUntil(() => values.Count == 1);
        Assert.Equal([42u], values);
        output.Destroy();
    }

    [Fact]
    public void An_observed_change_updates_tryget_and_raises_the_event()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, edid, observed: 60);
        Assert.True(manager.Control.TryGet(output, out var value));
        Assert.Equal(60u, value);

        IOutput? changed = null;
        manager.Control.BrightnessChanged += o => changed = o;
        device.SetObservedBrightness(30);
        device.Commit();
        host.PumpToServer();

        Assert.Equal(output, changed);
        Assert.True(manager.Control.TryGet(output, out value));
        Assert.Equal(30u, value);
        output.Destroy();
    }

    [Fact]
    public void A_ddc_ci_device_coalesces_five_sets_into_one_event_with_the_last_value()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, edid, ddcCi: true);
        var values = new List<uint>();
        device.RequestedBrightness += (_, e) => values.Add(e.Value);

        for (var i = 1u; i <= 5; i++)
        {
            manager.Control.Set(output, i * 10);
        }

        host.PumpToClient();
        Assert.Empty(values);

        Thread.Sleep(150);
        host.PumpUntil(() => values.Count == 1);
        Assert.Equal([50u], values);
        output.Destroy();
    }

    [Fact]
    public void A_non_ddc_ci_device_sends_every_request()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, edid);
        var values = new List<uint>();
        device.RequestedBrightness += (_, e) => values.Add(e.Value);

        for (var i = 1u; i <= 5; i++)
        {
            manager.Control.Set(output, i * 10);
        }

        host.PumpUntil(() => values.Count == 5);
        Assert.Equal([10u, 20u, 30u, 40u, 50u], values);
        output.Destroy();
    }

    [Fact]
    public void A_consumer_registration_wins_over_the_module_default_in_either_order()
    {
        using var host = new CompositorTestHost();
        var own = new NullBrightness();

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Use<IOutputBrightness>(own);
            services.Install(new ExternalBrightnessModule());
            services.Freeze();
            Assert.Same(own, services.Find<IOutputBrightness>());
        }

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Install(new ExternalBrightnessModule());
            services.Use<IOutputBrightness>(own);
            services.Freeze();
            Assert.Same(own, services.Find<IOutputBrightness>());
        }
    }

    [Fact]
    public void Destroying_the_device_unregisters_it()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));

        var proxy = Bind(host);
        var device = Register(host, proxy, edid);
        Assert.True(manager.Control.Supports(output));

        device.Destroy();
        host.PumpToServer();
        Assert.False(manager.Control.Supports(output));
        output.Destroy();
    }

    [Fact]
    public void The_multiplier_converts_to_the_device_scale_at_the_boundaries()
    {
        using var host = new CompositorTestHost();
        var edid = Edid(7);
        var output = new TestEdidOutput("DP-3", edid);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var manager = new ExternalBrightnessManager(host.Display, host.Loop, new LayoutOutputSet(layout));
        var configuration = new Basin.Color.ColorOutputConfiguration(new LayoutOutputConfiguration(layout))
        {
            Brightness = manager.Control,
        };

        var proxy = Bind(host);
        var device = Register(host, proxy, edid);
        var values = new List<uint>();
        device.RequestedBrightness += (_, e) => values.Add(e.Value);

        foreach (var multiplier in new uint[] { 0, 5000, 10000 })
        {
            Assert.True(configuration.Apply(
            [
                new OutputConfigurationEntry { Output = output, Enabled = true, Brightness = multiplier },
            ]));
        }

        host.PumpUntil(() => values.Count == 3);
        Assert.Equal([0u, 50u, 100u], values);
        output.Destroy();
    }

    private sealed class NullBrightness : IOutputBrightness
    {
        public event Action<IOutput>? BrightnessChanged;

        public bool Supports(IOutput output) => false;

        public uint Max(IOutput output) => 0;

        public bool TryGet(IOutput output, out uint value)
        {
            value = 0;
            return false;
        }

        public bool Set(IOutput output, uint value)
        {
            BrightnessChanged?.Invoke(output);
            return false;
        }
    }
}
