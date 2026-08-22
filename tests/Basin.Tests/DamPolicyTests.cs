using System.Runtime.InteropServices;
using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DamPolicyTests
{
    private const uint BtnLeft = 0x110;
    private const uint StateMaximized = 1;
    private const uint StateFullscreen = 2;

    private sealed class ClientToplevel
    {
        public required WlSurface Surface { get; init; }

        public required Basin.Shell.Xdg.Protocol.XdgSurface Xdg { get; init; }

        public required Basin.Shell.Xdg.Protocol.XdgToplevel Toplevel { get; init; }

        public uint Serial { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public uint[] States { get; set; } = [];

        public uint[] Capabilities { get; set; } = [];
    }

    private static ClientToplevel CreateToplevel(ShmTestClient client)
    {
        var surface = client.Compositor.CreateSurface();
        var xdg = client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdg.GetToplevel();
        var entry = new ClientToplevel { Surface = surface, Xdg = xdg, Toplevel = toplevel };
        xdg.Configure += (_, e) => entry.Serial = e.Serial;
        toplevel.Configure += (_, e) =>
        {
            entry.Width = e.Width;
            entry.Height = e.Height;
            entry.States = MemoryMarshal.Cast<byte, uint>(e.States).ToArray();
        };
        toplevel.WmCapabilitiesEvent += (_, e) =>
            entry.Capabilities = MemoryMarshal.Cast<byte, uint>(e.Capabilities).ToArray();
        return entry;
    }

    private static void Map(DamTestHost host, ClientToplevel entry, int width, int height)
    {
        if (entry.Serial == 0)
        {
            entry.Surface.Commit();
            host.PumpUntil(() => entry.Serial != 0);
        }

        entry.Xdg.AckConfigure(entry.Serial);
        var buffer = host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xFF446688));
        entry.Surface.Attach(buffer.Proxy, 0, 0);
        entry.Surface.Commit();
        host.PumpToClient();
        host.PumpToClient();
    }

    [Fact]
    public void Primary_toplevel_is_maximized_to_the_layout()
    {
        using var host = new DamTestHost();
        var entry = CreateToplevel(host.Client);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);

        Assert.Equal((1280, 720), (entry.Width, entry.Height));
        Assert.Contains(StateMaximized, entry.States);

        Map(host, entry, 1280, 720);
        var view = Assert.Single(host.Dam.Views.Views);
        Assert.Equal((0, 0), (view.Scene.Tree.X, view.Scene.Tree.Y));
        entry.Surface.Destroy();
    }

    [Fact]
    public void Transient_toplevel_is_centered()
    {
        using var host = new DamTestHost();
        var primary = CreateToplevel(host.Client);
        primary.Surface.Commit();
        host.PumpUntil(() => primary.Serial != 0);
        Map(host, primary, 1280, 720);

        var dialog = CreateToplevel(host.Client);
        dialog.Toplevel.SetParent(primary.Toplevel);
        dialog.Surface.Commit();
        host.PumpUntil(() => dialog.Serial != 0);
        Assert.Equal((0, 0), (dialog.Width, dialog.Height));
        Assert.DoesNotContain(StateMaximized, dialog.States);

        Map(host, dialog, 300, 200);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 2);
        var view = host.Dam.Views.Views[0];
        Assert.False(view.IsPrimary);
        Assert.Equal(((1280 - 300) / 2, (720 - 200) / 2), (view.Scene.Tree.X, view.Scene.Tree.Y));
        dialog.Surface.Destroy();
        primary.Surface.Destroy();
    }

    [Fact]
    public void Oversized_transient_is_maximized()
    {
        using var host = new DamTestHost();
        var primary = CreateToplevel(host.Client);
        primary.Surface.Commit();
        host.PumpUntil(() => primary.Serial != 0);
        Map(host, primary, 1280, 720);

        var dialog = CreateToplevel(host.Client);
        dialog.Toplevel.SetParent(primary.Toplevel);
        dialog.Surface.Commit();
        host.PumpUntil(() => dialog.Serial != 0);
        Map(host, dialog, 1400, 800);

        host.PumpUntil(() => host.Dam.Views.Views.Count == 2 && dialog.Width == 1280);
        var view = host.Dam.Views.Views[0];
        Assert.Equal((0, 0), (view.Scene.Tree.X, view.Scene.Tree.Y));
        Assert.Contains(StateMaximized, dialog.States);
        dialog.Surface.Destroy();
        primary.Surface.Destroy();
    }

    [Fact]
    public void Focus_follows_map_and_falls_back_on_destroy()
    {
        using var host = new DamTestHost();
        var first = CreateToplevel(host.Client);
        first.Surface.Commit();
        host.PumpUntil(() => first.Serial != 0);
        Map(host, first, 1280, 720);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 1);
        var firstView = host.Dam.Views.Views[0];
        Assert.Same(firstView, host.Dam.Views.FocusedView);

        var second = CreateToplevel(host.Client);
        second.Surface.Commit();
        host.PumpUntil(() => second.Serial != 0);
        Map(host, second, 1280, 720);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 2);
        Assert.NotSame(firstView, host.Dam.Views.FocusedView);

        second.Toplevel.Destroy();
        second.Xdg.Destroy();
        second.Surface.Destroy();
        host.PumpUntil(() => host.Dam.Views.Views.Count == 1);
        Assert.Same(firstView, host.Dam.Views.FocusedView);
        first.Surface.Destroy();
    }

    [Fact]
    public void Click_does_not_steal_focus_from_a_modal()
    {
        using var host = new DamTestHost();
        var primary = CreateToplevel(host.Client);
        primary.Surface.Commit();
        host.PumpUntil(() => primary.Serial != 0);
        Map(host, primary, 1280, 720);

        var dialog = CreateToplevel(host.Client);
        dialog.Toplevel.SetParent(primary.Toplevel);
        dialog.Surface.Commit();
        host.PumpUntil(() => dialog.Serial != 0);
        Map(host, dialog, 300, 200);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 2);

        var dialogView = host.Dam.Views.FocusedView;
        Assert.NotNull(dialogView);
        Assert.False(dialogView.IsPrimary);

        host.Dam.DamSeat.Warp(10, 10);
        host.Dam.DamSeat.OnButton(0, BtnLeft, true);
        host.Dam.DamSeat.OnButton(0, BtnLeft, false);
        host.PumpToClient();

        Assert.Same(dialogView, host.Dam.Views.FocusedView);
        dialog.Surface.Destroy();
        primary.Surface.Destroy();
    }

    [Fact]
    public void Extend_mode_spans_both_outputs()
    {
        using var host = new DamTestHost();
        host.Dam.Outputs.AddView(host.Dam.Host.Headless!.CreateOutput(new OutputMode(800, 600, 60_000)));
        Assert.Equal(new Box(0, 0, 2080, 720), host.Dam.Layout.Bounds);

        var entry = CreateToplevel(host.Client);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);
        Assert.Equal((2080, 720), (entry.Width, entry.Height));
        entry.Surface.Destroy();
    }

    [Fact]
    public void Last_mode_disables_the_previous_output_and_reenables_on_removal()
    {
        using var host = new DamTestHost(lastOutputOnly: true);
        var first = host.Dam.Outputs.Views[0];

        var entry = CreateToplevel(host.Client);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);
        Map(host, entry, 1280, 720);
        Assert.Equal((1280, 720), (entry.Width, entry.Height));

        var second = host.Dam.Outputs.AddView(
            host.Dam.Host.Headless!.CreateOutput(new OutputMode(800, 600, 60_000)));
        Assert.False(first.Output.Enabled);
        Assert.Equal(new Box(0, 0, 800, 600), host.Dam.Layout.Bounds);
        host.PumpUntil(() => entry.Width == 800 && entry.Height == 600);

        host.Dam.Outputs.RemoveView(second);
        Assert.True(first.Output.Enabled);
        Assert.Equal(new Box(0, 0, 1280, 720), host.Dam.Layout.Bounds);
        host.PumpUntil(() => entry.Width == 1280 && entry.Height == 720);
        entry.Surface.Destroy();
    }

    [Fact]
    public void Fullscreen_sizes_before_it_states()
    {
        using var host = new DamTestHost();
        var entry = CreateToplevel(host.Client);
        entry.Toplevel.SetFullscreen(null);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);

        Assert.Equal((1280, 720), (entry.Width, entry.Height));
        Assert.Contains(StateFullscreen, entry.States);
        entry.Surface.Destroy();
    }

    [Fact]
    public void Wm_capabilities_are_fullscreen_only()
    {
        using var host = new DamTestHost();
        var entry = CreateToplevel(host.Client);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0);
        Assert.Equal([3u], entry.Capabilities);
        entry.Surface.Destroy();
    }

    [Fact]
    public void Decorations_flag_forces_server_mode()
    {
        using var host = new DamTestHost(serverDecorations: true);
        var entry = CreateToplevel(host.Client);
        var decoration = host.Client.DecorationManager!.GetToplevelDecoration(entry.Toplevel);
        var modes = new List<uint>();
        decoration.Configure += (_, e) => modes.Add((uint)e.Mode);
        entry.Surface.Commit();
        host.PumpUntil(() => entry.Serial != 0 && modes.Count > 0);
        Assert.Equal(2u, modes[^1]);

        decoration.SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ClientSide);
        host.PumpToClient();
        host.PumpToClient();
        Assert.Equal(2u, modes[^1]);
        decoration.Destroy();
        entry.Surface.Destroy();
    }

    [Fact]
    public void A_touch_tap_on_a_touch_client_is_delivered_and_focuses()
    {
        using var host = new DamTestHost();
        var buttons = 0;
        var pointer = host.Client.Seat!.GetPointer();
        pointer.Button += (_, _) => buttons++;
        var touch = host.Client.Seat!.GetTouch();
        var touches = 0;
        touch.Down += (_, _) => touches++;

        var first = CreateToplevel(host.Client);
        first.Surface.Commit();
        host.PumpUntil(() => first.Serial != 0);
        Map(host, first, 1280, 720);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 1);
        var firstView = host.Dam.Views.Views[0];

        var second = CreateToplevel(host.Client);
        second.Surface.Commit();
        host.PumpUntil(() => second.Serial != 0);
        Map(host, second, 300, 200);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 2);
        Assert.NotSame(firstView, host.Dam.Views.FocusedView);
        host.PumpToServer();

        host.Dam.DamSeat.TouchRouter.Down(0, 0, 1200, 700);
        host.Dam.DamSeat.TouchRouter.Frame();
        host.Dam.DamSeat.TouchRouter.Up(1, 0);
        host.Dam.DamSeat.TouchRouter.Frame();
        host.PumpUntil(() => touches == 1);
        host.PumpToClient();

        Assert.Same(firstView, host.Dam.Views.FocusedView);
        Assert.Equal(0, buttons);
        second.Surface.Destroy();
        first.Surface.Destroy();
    }

    [Fact]
    public void A_touch_tap_on_a_client_without_touch_drives_the_pointer()
    {
        using var host = new DamTestHost();
        var buttons = 0;
        var pointer = host.Client.Seat!.GetPointer();
        pointer.Button += (_, _) => buttons++;

        var first = CreateToplevel(host.Client);
        first.Surface.Commit();
        host.PumpUntil(() => first.Serial != 0);
        Map(host, first, 1280, 720);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 1);
        var firstView = host.Dam.Views.Views[0];

        var second = CreateToplevel(host.Client);
        second.Surface.Commit();
        host.PumpUntil(() => second.Serial != 0);
        Map(host, second, 300, 200);
        host.PumpUntil(() => host.Dam.Views.Views.Count == 2);
        Assert.NotSame(firstView, host.Dam.Views.FocusedView);
        host.PumpToServer();

        host.Dam.DamSeat.TouchRouter.Down(0, 0, 1200, 700);
        host.Dam.DamSeat.TouchRouter.Frame();
        host.Dam.DamSeat.TouchRouter.Up(1, 0);
        host.Dam.DamSeat.TouchRouter.Frame();
        host.PumpUntil(() => buttons == 2);

        Assert.Same(firstView, host.Dam.Views.FocusedView);
        second.Surface.Destroy();
        first.Surface.Destroy();
    }

    [Fact]
    public void Teardown_is_clean()
    {
        using (var host = new DamTestHost())
        {
            var entry = CreateToplevel(host.Client);
            entry.Surface.Commit();
            host.PumpUntil(() => entry.Serial != 0);
            Map(host, entry, 1280, 720);
        }

        Assert.SkipWhen(!Basin.Diagnostics.BasinCounters.Enabled, "lifetime tracking is compiled out in this configuration");
        Assert.Equal(0, Basin.Diagnostics.BasinCounters.LiveObjects);
    }
}
