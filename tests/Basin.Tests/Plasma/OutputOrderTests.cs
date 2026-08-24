using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Plasma;
using Xunit;

namespace Basin.Tests;

public sealed class OutputOrderTests
{
    private sealed class OrderView
    {
        public readonly List<string> Current = [];
        public readonly List<List<string>> Lists = [];
        public int DoneCount;
    }

    private static OrderView BindOrder(CompositorTestHost host)
    {
        OrderView? view = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_output_order_v1")
            {
                var proxy = registry.Bind<Basin.Plasma.Protocol.KdeOutputOrderV1>(e.Name, 1);
                view = new OrderView();
                proxy.Output += (_, oe) => view.Current.Add(oe.OutputName);
                proxy.Done += (_, _) =>
                {
                    view.Lists.Add(new List<string>(view.Current));
                    view.Current.Clear();
                    view.DoneCount++;
                };
            }
        };
        host.PumpToClient();
        Assert.NotNull(view);
        return view!;
    }

    private sealed class TestOutputOrder : IOutputOrder
    {
        public readonly List<IOutput> Ordered = [];

        public event Action? Changed;

        public int Enumerate(Span<IOutput> outputs)
        {
            if (outputs.Length < Ordered.Count)
            {
                return -1;
            }

            for (var i = 0; i < Ordered.Count; i++)
            {
                outputs[i] = Ordered[i];
            }

            return Ordered.Count;
        }

        public void Raise() => Changed?.Invoke();
    }

    [Fact]
    public void A_bind_sends_one_output_per_enabled_output_then_done()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        using var manager = new OutputOrderManager(
            host.Display, new LayoutOutputOrder(new LayoutOutputSet(host.Layout), host.Layout));

        var view = BindOrder(host);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal(1, view.DoneCount);
        Assert.Equal([host.Output.Name, second.Name], view.Lists[0]);
    }

    [Fact]
    public void A_disabled_output_is_not_in_the_list()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        using (var disable = new OutputState())
        {
            second.Commit(disable.SetEnabled(false));
        }

        using var manager = new OutputOrderManager(
            host.Display, new LayoutOutputOrder(new LayoutOutputSet(host.Layout), host.Layout));

        var view = BindOrder(host);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal([host.Output.Name], view.Lists[0]);
    }

    [Fact]
    public void A_layout_change_resends_the_whole_list_to_every_bound_resource()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        using var manager = new OutputOrderManager(
            host.Display, new LayoutOutputOrder(new LayoutOutputSet(host.Layout), host.Layout));

        var first = BindOrder(host);
        var other = BindOrder(host);
        host.PumpUntil(() => first.DoneCount >= 1 && other.DoneCount >= 1);

        host.Layout.Move(second, -160, 0);
        host.PumpUntil(() => first.DoneCount >= 2 && other.DoneCount >= 2);

        Assert.Equal([second.Name, host.Output.Name], first.Lists[^1]);
        Assert.Equal([second.Name, host.Output.Name], other.Lists[^1]);
    }

    [Fact]
    public void The_default_order_sorts_left_to_right_then_top_to_bottom_ties_by_name()
    {
        using var host = new CompositorTestHost();
        var mode = new OutputMode(160, 120, 60_000);
        var b = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "B");
        var a = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "A");
        var c = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "C");
        var d = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "D");
        var layout = new OutputLayout();
        layout.Add(b, 100, 0);
        layout.Add(a, 100, 0);
        layout.Add(c, 0, 50);
        layout.Add(d, 0, 0);
        var order = new LayoutOutputOrder(new LayoutOutputSet(layout), layout);

        var outputs = new IOutput[4];
        var count = order.Enumerate(outputs);

        Assert.Equal(4, count);
        Assert.Equal([d, c, a, b], outputs);
    }

    [Fact]
    public void A_consumer_order_wins_over_the_default_in_both_registration_orders()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        var custom = new TestOutputOrder { Ordered = { second, host.Output } };

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Use(host.Layout).Use<IOutputOrder>(custom).Install(new OutputOrderModule()).Freeze();
            Assert.Same(custom, services.Find<IOutputOrder>());
            var view = BindOrder(host);
            host.PumpUntil(() => view.DoneCount >= 1);
            Assert.Equal([second.Name, host.Output.Name], view.Lists[0]);
        }

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Install(new OutputOrderModule()).Use(host.Layout).Use<IOutputOrder>(custom).Freeze();
            Assert.Same(custom, services.Find<IOutputOrder>());
            var view = BindOrder(host);
            host.PumpUntil(() => view.DoneCount >= 1);
            Assert.Equal([second.Name, host.Output.Name], view.Lists[0]);
        }
    }

    [Fact]
    public void An_empty_order_sends_done_alone()
    {
        using var host = new CompositorTestHost();
        using var manager = new OutputOrderManager(host.Display, new TestOutputOrder());

        var view = BindOrder(host);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal(1, view.DoneCount);
        Assert.Empty(view.Lists[0]);
    }

    [Fact]
    public void Enumerate_with_a_short_span_returns_negative_and_writes_nothing_past_the_end()
    {
        using var host = new CompositorTestHost();
        var mode = new OutputMode(160, 120, 60_000);
        var second = host.Backend.CreateOutput(mode, manualFrameClock: true);
        var third = host.Backend.CreateOutput(mode, manualFrameClock: true);
        host.Layout.Add(second, 160, 0);
        host.Layout.Add(third, 320, 0);
        var order = new LayoutOutputOrder(new LayoutOutputSet(host.Layout), host.Layout);

        var outputs = new IOutput[3];
        var count = order.Enumerate(outputs.AsSpan(0, 2));

        Assert.True(count < 0);
        Assert.Null(outputs[2]);
    }

    [Fact]
    public void The_names_match_wl_output_name_exactly_for_every_output()
    {
        using var host = new CompositorTestHost();
        var mode = new OutputMode(160, 120, 60_000);
        var left = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "DP-1");
        var right = host.Backend.CreateOutput(mode, manualFrameClock: true, name: "DP-2");
        var layout = new OutputLayout();
        layout.Add(left, 0, 0);
        layout.Add(right, 160, 0);
        using var manager = new OutputOrderManager(
            host.Display, new LayoutOutputOrder(new LayoutOutputSet(layout), layout));

        var view = BindOrder(host);
        host.PumpUntil(() => view.DoneCount >= 1);

        Assert.Equal([left.Name, right.Name], view.Lists[0]);
        Assert.Equal("DP-1", left.Name);
        Assert.Equal("DP-2", right.Name);
    }
}
