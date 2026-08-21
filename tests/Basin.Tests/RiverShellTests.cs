using System.Runtime.InteropServices;
using Basin.Backend.Headless;
using Basin.Scene;
using Basin.Shell.River;
using Xunit;
using Wm = Basin.WindowManager;

namespace Basin.Tests;

public class RiverShellTests
{
    [Fact]
    public void A_window_manager_binds_and_gets_a_manage_sequence()
    {
        using var fixture = new RiverFixture();

        Assert.True(fixture.Server.HasWindowManager);
        Assert.True(fixture.ManageSequences > 0);
        Assert.True(fixture.RenderSequences > 0);
        Assert.Equal(5u, fixture.Client.Version);
    }

    [Fact]
    public void The_manager_can_bind_the_wl_output_and_wl_seat_it_is_handed()
    {
        using var fixture = new RiverFixture();

        var output = Assert.Single(fixture.Outputs);
        var seat = Assert.Single(fixture.Seats);

        Assert.NotEqual(0u, output.WlOutputName);
        Assert.NotEqual(0u, seat.WlSeatName);

        var boundOutput = fixture.Client.Registry.Bind<Wayland.WlOutput>(output.WlOutputName, 4);
        var outputName = string.Empty;
        boundOutput.Name += (_, e) => outputName = e.Name;

        var boundSeat = fixture.Client.Registry.Bind<Wayland.WlSeat>(seat.WlSeatName, 5);
        var seatName = string.Empty;
        boundSeat.Name += (_, e) => seatName = e.Name;

        fixture.Settle();
        Assert.Equal(fixture.Host.Output.Name, outputName);
        Assert.Equal(fixture.Host.Seat.Name, seatName);

        boundOutput.Dispose();
        boundSeat.Dispose();
        fixture.Settle();
    }

    [Fact]
    public void An_output_reaches_the_manager_with_its_position_and_size()
    {
        using var fixture = new RiverFixture();

        var output = Assert.Single(fixture.Outputs);
        Assert.Equal(new Wm.Size(160, 120), output.Dimensions);
        Assert.Equal(new Wm.Point(0, 0), output.Position);
        Assert.False(output.IsRemoved);
    }

    [Fact]
    public void A_seat_reaches_the_manager()
    {
        using var fixture = new RiverFixture();
        Assert.Single(fixture.Seats);
    }

    [Fact]
    public void A_mapped_toplevel_arrives_as_a_new_window()
    {
        using var fixture = new RiverFixture();
        var appIds = new List<string?>();
        fixture.OnManage = context =>
        {
            foreach (var window in context.NewWindows)
            {
                appIds.Add(window.AppId);
            }
        };

        fixture.MapToplevel();

        var window = Assert.Single(fixture.Windows);
        Assert.Equal("basin-test", window.AppId);
        Assert.Contains("basin-test", appIds);
    }

    [Fact]
    public void Proposed_dimensions_reach_the_client_and_come_back_as_dimensions()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(90, 70);
            }
        };
        fixture.RequestManageAndSettle();

        Assert.Equal(90, toplevel.ConfiguredWidth);
        Assert.Equal(70, toplevel.ConfiguredHeight);

        fixture.PaintToplevel(toplevel, 90, 70);
        fixture.Settle();

        var window = Assert.Single(fixture.Windows);
        Assert.Equal(new Wm.Size(90, 70), window.Dimensions);
    }

    [Fact]
    public void Closing_a_window_reaches_the_manager_as_a_closed_window()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        Assert.Single(fixture.Windows);

        var closed = 0;
        fixture.OnManage = context => closed += context.ClosedWindows.Count;

        toplevel.Toplevel.Destroy();
        toplevel.XdgSurface.Destroy();
        fixture.Host.PumpToServer();
        fixture.Settle();

        Assert.Equal(1, closed);
        Assert.Empty(fixture.Windows);
    }

    [Fact]
    public void A_second_manager_is_told_window_management_is_unavailable()
    {
        using var fixture = new RiverFixture();
        using var second = fixture.ConnectManager();

        var unavailable = false;
        second.Unavailable += () => unavailable = true;
        fixture.Settle();

        Assert.True(unavailable);
        Assert.True(fixture.Server.HasWindowManager);
        Assert.True(fixture.ManageSequences > 0);
    }

    [Fact]
    public void A_management_request_outside_a_manage_sequence_is_a_protocol_error()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        var window = Assert.Single(fixture.Windows);
        var error = Assert.Throws<InvalidOperationException>(() => window.ProposeDimensions(10, 10));
        Assert.Contains("manage sequence", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rendering_request_outside_any_sequence_is_refused()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        var window = Assert.Single(fixture.Windows);
        var error = Assert.Throws<InvalidOperationException>(() => window.Node.SetPosition(1, 1));
        Assert.Contains("manage or render sequence", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_compositor_runs_the_same_sequences_with_no_manager_bound()
    {
        using var host = new CompositorTestHost(160, 120);
        var windowTree = new SceneTree(host.Scene.Root);
        using var server = new RiverWindowManager(
            host.Display, host.Loop, host.Scene, windowTree, host.Shell, host.Layout, [host.Seat]);

        server.AddOutput(host.OutputGlobal);
        var toplevel = MappedToplevel.Map(host, host.Client, 40, 40);

        for (var i = 0; i < 10; i++)
        {
            host.PumpToClient();
        }

        Assert.False(server.HasWindowManager);
        Assert.True(toplevel.ServerToplevel.IsMapped);
        windowTree.Destroy();
    }

    private const uint SuperKey = 125;

    private const uint QKey = 16;

    [Fact]
    public void A_bound_key_fires_the_binding_and_is_not_delivered_to_the_focused_surface()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        Wm.KeyBinding? binding = null;
        var presses = 0;
        var releases = 0;
        fixture.OnManage = _ =>
        {
            if (binding is not null)
            {
                return;
            }

            binding = fixture.Client.Bindings.Bind(seat, "q", Wm.Modifiers.Super);
            binding.Pressed += () => presses++;
            binding.Released += () => releases++;
            binding.Enable();
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(binding);

        Assert.False(fixture.PressKey(SuperKey), "a modifier alone must not be claimed");
        Assert.True(fixture.PressKey(QKey), "the bound key must be claimed");
        Assert.True(fixture.ReleaseKey(QKey));
        fixture.Settle();

        Assert.Equal(1, presses);
        Assert.Equal(1, releases);

        fixture.ReleaseKey(SuperKey);
        Assert.False(fixture.PressKey(QKey));
        fixture.ReleaseKey(QKey);
        fixture.Settle();
        Assert.Equal(1, presses);
    }

    [Fact]
    public void A_disabled_binding_does_not_claim_its_key()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        Wm.KeyBinding? binding = null;
        var presses = 0;
        fixture.OnManage = _ =>
            binding ??= fixture.Client.Bindings.Bind(seat, "q", Wm.Modifiers.Super, () => presses++);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        fixture.PressKey(SuperKey);
        Assert.False(fixture.PressKey(QKey));
        fixture.ReleaseKey(QKey);
        fixture.Settle();
        Assert.Equal(0, presses);

        fixture.OnManage = _ => binding!.Enable();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.True(fixture.PressKey(QKey));
        fixture.ReleaseKey(QKey);
        fixture.Settle();
        Assert.Equal(1, presses);
    }

    [Fact]
    public void An_eaten_key_is_withheld_and_reported_as_unbound()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        var ate = 0;
        fixture.Client.Bindings.AteUnboundKey += _ => ate++;

        var armed = false;
        fixture.OnManage = _ =>
        {
            if (!armed)
            {
                armed = true;
                fixture.Client.Bindings.EnsureNextKeyEaten(seat);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.True(fixture.PressKey(QKey));
        Assert.True(fixture.ReleaseKey(QKey));
        fixture.Settle();
        Assert.Equal(1, ate);

        Assert.False(fixture.PressKey(QKey));
        fixture.ReleaseKey(QKey);
        fixture.Settle();
        Assert.Equal(1, ate);
    }

    [Fact]
    public void Borders_are_drawn_around_the_window_content()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(40, 30);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(20, 20);
                window.SetBorders(Wm.Edges.All, 4, Wm.WmColor.FromRgba(0xff, 0x00, 0x00));
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.Host.RenderFrame();

        Assert.Equal(Red, fixture.Host.Pixel(20, 18));
        Assert.Equal(Red, fixture.Host.Pixel(20, 51));
        Assert.Equal(Red, fixture.Host.Pixel(18, 30));
        Assert.Equal(Red, fixture.Host.Pixel(61, 30));

        Assert.Equal(Red, fixture.Host.Pixel(17, 17));

        Assert.NotEqual(Red, fixture.Host.Pixel(15, 30));
    }

    [Fact]
    public void A_border_on_one_edge_does_not_reach_past_an_unbordered_neighbour()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(40, 30);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(20, 20);

                window.SetBorders(Wm.Edges.Left, 4, Wm.WmColor.FromRgba(0xff, 0x00, 0x00));
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.Host.RenderFrame();

        Assert.Equal(Red, fixture.Host.Pixel(18, 30));
        Assert.NotEqual(Red, fixture.Host.Pixel(18, 17));
        Assert.NotEqual(Red, fixture.Host.Pixel(30, 18));
    }

    [Fact]
    public void A_buffer_larger_than_its_geometry_lands_content_first_at_the_wm_position()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(40, 40);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(20, 20);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var buffer = fixture.Host.Client.CreateBuffer(80, 80, Fill.Solid(80, 80, Green));
        toplevel.XdgSurface.SetWindowGeometry(20, 20, 40, 40);
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, 80, 80);
        toplevel.Surface.Commit();
        fixture.Settle();
        fixture.Host.RenderFrame();

        Assert.Equal(Green, fixture.Host.Pixel(5, 5));
        Assert.Equal(Green, fixture.Host.Pixel(59, 59));
        Assert.NotEqual(Green, fixture.Host.Pixel(95, 95));

        var window = Assert.Single(fixture.Windows);
        Assert.Equal(new Wm.Size(40, 40), window.Dimensions);
    }

    [Fact]
    public void A_popup_renders_above_its_window_at_the_placed_offset()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(40, 40);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(20, 20);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var client = fixture.Host.Client;
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(30, 20);
        positioner.SetAnchorRect(5, 5, 1, 1);
        positioner.SetAnchor(Basin.Shell.Xdg.Protocol.XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(Basin.Shell.Xdg.Protocol.XdgPositioner.Gravity.BottomRight);

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popup = popupXdg.GetPopup(toplevel.XdgSurface, positioner);
        var configured = false;
        popupXdg.Configure += (_, e) =>
        {
            popupXdg.AckConfigure(e.Serial);
            configured = true;
        };
        popupSurface.Commit();
        fixture.Settle();
        Assert.True(configured);

        var buffer = client.CreateBuffer(30, 20, Fill.Solid(30, 20, Green));
        popupSurface.Attach(buffer.Proxy, 0, 0);
        popupSurface.Damage(0, 0, 30, 20);
        popupSurface.Commit();
        fixture.Settle();
        fixture.DropStrayHostScenes();
        fixture.Host.RenderFrame();

        Assert.Equal(Green, fixture.Host.Pixel(27, 27));
        Assert.Equal(Green, fixture.Host.Pixel(54, 44));
        Assert.NotEqual(Green, fixture.Host.Pixel(24, 24));

        popup.Destroy();
        popupXdg.Destroy();
        popupSurface.Destroy();
        fixture.Settle();
    }

    [Fact]
    public void The_window_content_box_is_reported_for_capture()
    {
        using var fixture = new RiverFixture();
        using var source = new Basin.Shell.Xdg.XdgToplevelSource(fixture.Host.Shell);
        fixture.Server.ToplevelSource = source;
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(40, 40);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(20, 20);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        fixture.PaintToplevel(toplevel, 40, 40);
        fixture.Settle();

        var id = source.IdFor(toplevel.ServerToplevel);
        Assert.True(source.TryGet(id, out var info));
        Assert.Equal(new Box(20, 20, 40, 40), info.Geometry);

        Assert.True(fixture.Server.TryCaptureTrees(toplevel.ServerToplevel.Surface, out var content, out var popups));
        Assert.NotNull(content);
        Assert.NotNull(popups);
    }

    [Fact]
    public void A_shell_surface_gets_a_node_and_renders_where_it_is_placed()
    {
        using var fixture = new RiverFixture();

        var surface = fixture.Client.Compositor!.CreateSurface();
        var shell = fixture.Client.CreateShellSurface(surface);
        fixture.Settle();

        var buffer = fixture.CreateManagerBuffer(30, 10, Green);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 10);
        surface.Commit();
        fixture.Settle();

        fixture.OnManage = _ => shell.Node.SetPosition(60, 4);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        fixture.Host.RenderFrame();

        Assert.Equal(Green, fixture.Host.Pixel(62, 6));
        Assert.NotEqual(Green, fixture.Host.Pixel(62, 20));
        shell.Dispose();
    }

    [Fact]
    public void Input_on_a_decoration_is_an_interaction_with_the_window_it_frames()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var window = Assert.Single(fixture.Windows);
        var seat = Assert.Single(fixture.Seats);

        var interactions = new List<Wm.WmWindow>();
        seat.WindowInteraction += interactions.Add;

        var surface = fixture.Client.Compositor!.CreateSurface();
        Wm.WmDecoration? decoration = null;
        fixture.OnManage = _ => decoration ??= window.CreateDecorationAbove(surface);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(decoration);

        var buffer = fixture.CreateManagerBuffer(30, 10, Green);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 10);
        surface.Commit();
        fixture.Settle();

        var hit = fixture.Host.Scene.SurfaceAt(5, 5);
        Assert.NotNull(hit?.Surface);
        Assert.NotEqual(toplevel.ServerToplevel.Surface, hit!.Value.Surface);

        fixture.Server.NotifyInteraction(fixture.Host.Seat, hit.Value.Surface!);
        fixture.Settle();

        Assert.Equal([window], interactions);
        decoration.Dispose();
        surface.Destroy();
        fixture.Settle();
    }

    [Fact]
    public void Input_on_a_shell_surface_is_an_interaction_with_it()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        var interactions = new List<Wm.WmShellSurface>();
        seat.ShellSurfaceInteraction += interactions.Add;

        var surface = fixture.Client.Compositor!.CreateSurface();
        var shell = fixture.Client.CreateShellSurface(surface);
        fixture.Settle();

        var buffer = fixture.CreateManagerBuffer(30, 10, Green);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 10);
        surface.Commit();
        fixture.Settle();

        var hit = fixture.Host.Scene.SurfaceAt(5, 5);
        Assert.NotNull(hit?.Surface);

        fixture.Server.NotifyInteraction(fixture.Host.Seat, hit!.Value.Surface!);
        fixture.Settle();

        Assert.Equal([shell], interactions);
        shell.Dispose();
        surface.Destroy();
        fixture.Settle();
    }

    [Fact]
    public void A_client_that_never_speaks_xdg_decoration_only_supports_csd()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        var window = Assert.Single(fixture.Windows);
        Assert.Equal(Wm.DecorationHint.OnlySupportsClientSide, window.DecorationHint);
    }

    [Fact]
    public void A_client_decoration_preference_reaches_the_manager_as_a_hint()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        var decoration = fixture.Host.Client.DecorationManager!.GetToplevelDecoration(toplevel.Toplevel);
        fixture.Settle();
        var window = Assert.Single(fixture.Windows);
        Assert.Equal(Wm.DecorationHint.NoPreference, window.DecorationHint);

        decoration.SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ServerSide);
        fixture.Settle();
        Assert.Equal(Wm.DecorationHint.PrefersServerSide, window.DecorationHint);

        decoration.SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ClientSide);
        fixture.Settle();
        Assert.Equal(Wm.DecorationHint.PrefersClientSide, window.DecorationHint);

        decoration.Dispose();
        fixture.Settle();
    }

    [Fact]
    public void A_preference_stated_before_the_window_maps_still_reaches_the_manager()
    {
        using var fixture = new RiverFixture();

        fixture.MapToplevel(beforeMap: toplevel =>
        {
            var decoration = fixture.Host.Client.DecorationManager!.GetToplevelDecoration(toplevel);
            decoration.SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ServerSide);
        });

        var window = Assert.Single(fixture.Windows);
        Assert.Equal(Wm.DecorationHint.PrefersServerSide, window.DecorationHint);
    }

    [Fact]
    public void A_window_that_asks_for_fullscreen_before_it_maps_still_reaches_the_manager()
    {
        using var fixture = new RiverFixture();
        var fullscreen = 0;
        fixture.OnManage = context =>
        {
            foreach (var window in context.NewWindows)
            {
                window.FullscreenRequested += _ => fullscreen++;
            }
        };

        fixture.MapToplevel(beforeMap: toplevel => toplevel.SetFullscreen(null));
        fixture.Settle();
        fixture.OnManage = null;

        Assert.Equal(1, fullscreen);
    }

    [Fact]
    public void A_window_that_asks_to_be_maximized_before_it_maps_still_reaches_the_manager()
    {
        using var fixture = new RiverFixture();
        var maximized = 0;
        fixture.OnManage = context =>
        {
            foreach (var window in context.NewWindows)
            {
                window.MaximizeRequested += () => maximized++;
            }
        };

        fixture.MapToplevel(beforeMap: toplevel => toplevel.SetMaximized());
        fixture.Settle();
        fixture.OnManage = null;

        Assert.Equal(1, maximized);
    }

    [Fact]
    public void A_manager_asking_for_ssd_tells_the_client_to_stop_drawing_its_own()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        var decoration = fixture.Host.Client.DecorationManager!.GetToplevelDecoration(toplevel.Toplevel);
        var modes = new List<Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode>();
        decoration.Configure += (_, e) => modes.Add(e.Mode);
        fixture.Settle();
        Assert.Equal(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ClientSide, modes[^1]);

        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => window.UseServerSideDecorations();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.Equal(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ServerSide, modes[^1]);
        Assert.Equal(
            Basin.Shell.Xdg.DecorationMode.ServerSide,
            fixture.Host.Decorations.ModeOf(toplevel.ServerToplevel));

        var configures = modes.Count;
        fixture.RequestManageAndSettle();
        Assert.Equal(configures, modes.Count);

        fixture.OnManage = _ => window.UseClientSideDecorations();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.Equal(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ClientSide, modes[^1]);

        decoration.Dispose();
        fixture.Settle();
    }

    [Fact]
    public void A_held_commit_that_never_arrives_is_a_protocol_error()
    {
        using var fixture = new RiverFixture();

        var surface = fixture.Client.Compositor!.CreateSurface();
        var shell = fixture.Client.CreateShellSurface(surface);
        fixture.Settle();

        Exception? escaped = null;
        fixture.OnRender = _ =>
        {
            try
            {
                shell.SyncNextCommit();
            }
            catch (Exception error)
            {
                escaped = error;
            }
        };

        fixture.Client.RequestManage();
        fixture.Settle();
        fixture.OnRender = null;

        Assert.True(fixture.SyncViolations > 0 || escaped is not null || !fixture.Server.HasWindowManager);
        GC.KeepAlive(shell);
    }

    [Fact]
    public void Layer_shell_is_unsupported_until_a_manager_binds_it()
    {
        using var host = new CompositorTestHost(160, 120);
        var windowTree = new SceneTree(host.Scene.Root);
        using var server = new RiverWindowManager(
            host.Display, host.Loop, host.Scene, windowTree, host.Shell, host.Layout, [host.Seat]);

        Assert.False(server.LayerShell.IsSupported);
        Assert.Null(server.LayerShell.DefaultOutput);
        windowTree.Destroy();
    }

    [Fact]
    public void A_manager_that_binds_layer_shell_opts_layer_surfaces_in()
    {
        using var fixture = new RiverFixture();

        Assert.NotNull(fixture.Client.LayerShell);
        Assert.True(fixture.Server.LayerShell.IsSupported);
    }

    [Fact]
    public void The_non_exclusive_area_reaches_the_manager_in_global_coordinates()
    {
        using var fixture = new RiverFixture();
        var output = Assert.Single(fixture.Outputs);
        fixture.Client.LayerShell!.Track(output);
        fixture.Settle();

        fixture.Server.LayerShell.SetNonExclusiveArea(fixture.Host.Output, new Box(0, 20, 160, 100));
        fixture.Settle();

        Assert.Equal(new Wm.Rect(0, 20, 160, 100), output.NonExclusiveArea);
    }

    [Fact]
    public void A_layer_surface_with_exclusive_focus_locks_the_manager_out()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);
        fixture.Client.LayerShell!.Track(seat);
        fixture.Settle();

        var taken = 0;
        var released = 0;
        fixture.Client.LayerShell.FocusTaken += _ => taken++;
        fixture.Client.LayerShell.FocusReleased += _ => released++;

        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => seat.FocusWindow(window);
        fixture.RequestManageAndSettle();
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        fixture.Host.Seat.Keyboard.NotifyClearFocus();
        fixture.Server.LayerShell.SetLayerFocus(fixture.Host.Seat, LayerFocus.Exclusive);
        fixture.RequestManageAndSettle();
        Assert.Equal(1, taken);
        Assert.Null(fixture.Host.Seat.Keyboard.Focus);

        fixture.Server.LayerShell.SetLayerFocus(fixture.Host.Seat, LayerFocus.None);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.Equal(1, released);
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);
    }

    [Fact]
    public void An_exclusive_layer_surface_takes_the_focus_the_manager_is_denied()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);
        fixture.Client.LayerShell!.Track(seat);
        fixture.Settle();

        var panel = MapPanel(fixture, out var panelSurface);
        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => seat.FocusWindow(window);
        fixture.RequestManageAndSettle();
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        fixture.Server.LayerShell.SetLayerFocus(fixture.Host.Seat, LayerFocus.Exclusive, panelSurface);
        fixture.RequestManageAndSettle();
        Assert.Same(panelSurface, fixture.Host.Seat.Keyboard.Focus);

        fixture.Server.LayerShell.SetLayerFocus(fixture.Host.Seat, LayerFocus.None, null);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        panel.Dispose();
    }

    [Fact]
    public void A_non_exclusive_layer_surface_loses_to_a_manager_focusing_in_the_same_sequence()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);
        fixture.Client.LayerShell!.Track(seat);
        fixture.Settle();

        var released = 0;
        fixture.Client.LayerShell.FocusReleased += _ => released++;

        var panel = MapPanel(fixture, out var panelSurface);
        var window = Assert.Single(fixture.Windows);

        fixture.Server.LayerShell.SetLayerFocus(fixture.Host.Seat, LayerFocus.NonExclusive, panelSurface);
        fixture.RequestManageAndSettle();
        Assert.Same(panelSurface, fixture.Host.Seat.Keyboard.Focus);

        fixture.OnManage = _ => seat.FocusWindow(window);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        fixture.RequestManageAndSettle();
        Assert.Equal(1, released);

        panel.Dispose();
    }

    private static IDisposable MapPanel(RiverFixture fixture, out Basin.Surface serverSurface)
    {
        var shell = new Basin.Shell.Xdg.LayerShell(fixture.Host.Display, fixture.Host.Compositor);
        Basin.Shell.Xdg.LayerSurface? server = null;
        shell.NewSurface += layer =>
        {
            server = layer;
            layer.InitialCommit += () => layer.Configure(160, 30);
        };

        var client = fixture.Host.Client;
        Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1? shellProxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_layer_shell_v1")
            {
                shellProxy = registry.Bind<Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1>(e.Name, 4);
            }
        };
        fixture.Host.PumpToClient();
        Assert.NotNull(shellProxy);

        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy!.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(160, 30);
        layerProxy.Configure += (_, e) => layerProxy.AckConfigure(e.Serial);
        surface.Commit();
        fixture.Host.PumpUntil(() => server is not null);

        var buffer = client.CreateBuffer(160, 30, Fill.Solid(160, 30, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 160, 30);
        surface.Commit();
        fixture.Host.PumpUntil(() => server is { IsMapped: true });

        serverSurface = server!.Surface;
        return shell;
    }

    [Fact]
    public void A_locked_session_ignores_the_managers_focus_requests()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);
        var sessionLock = new Basin.Desktop.SessionLockManager(fixture.Host.Display, fixture.Host.Compositor);
        fixture.Server.SessionLock = sessionLock;

        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => seat.FocusWindow(window);
        fixture.RequestManageAndSettle();
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        var locker = fixture.Host.ConnectClient();
        var lockProxy = BindLockManager(fixture.Host, locker.Display).Lock();
        var gotLocked = false;
        lockProxy.Locked += (_, _) => gotLocked = true;
        fixture.Host.PumpUntil(() => gotLocked);

        fixture.Host.Seat.Keyboard.NotifyClearFocus();
        fixture.RequestManageAndSettle();
        Assert.Null(fixture.Host.Seat.Keyboard.Focus);

        lockProxy.UnlockAndDestroy();
        fixture.Host.PumpToServer();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.Equal(toplevel.ServerToplevel.Surface, fixture.Host.Seat.Keyboard.Focus);

        sessionLock.Dispose();
    }

    private static Basin.Desktop.Protocol.ExtSessionLockManagerV1 BindLockManager(
        CompositorTestHost host, Wayland.WlDisplay display)
    {
        Basin.Desktop.Protocol.ExtSessionLockManagerV1? manager = null;
        var registry = display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_session_lock_manager_v1")
            {
                manager = registry.Bind<Basin.Desktop.Protocol.ExtSessionLockManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(manager);
        return manager!;
    }

    [Fact]
    public void The_default_output_for_layer_surfaces_is_undefined_until_set()
    {
        using var fixture = new RiverFixture();
        var output = Assert.Single(fixture.Outputs);

        Assert.Null(fixture.Server.LayerShell.DefaultOutput);

        fixture.OnManage = _ => output.SetDefaultForLayerSurfaces();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.Same(fixture.Host.Output, fixture.Server.LayerShell.DefaultOutput);
    }

    [Fact]
    public void Only_the_topmost_of_two_fullscreen_windows_on_an_output_is_displayed()
    {
        using var fixture = new RiverFixture();
        var first = fixture.MapToplevel();
        var second = fixture.MapToplevel();
        Assert.Equal(2, fixture.Windows.Count);

        fixture.OnManage = context =>
        {
            var output = context.Outputs[0];
            foreach (var window in context.Windows)
            {
                window.Fullscreen(output);
            }

            context.Render.PlaceTop(context.Windows[1].Node);
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;

        Assert.Equal(1, fixture.Server.DisplayedFullscreenCount);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [Fact]
    public void A_fullscreen_window_is_clipped_to_its_output_and_draws_no_borders()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            var output = context.Outputs[0];
            foreach (var window in context.Windows)
            {
                window.Fullscreen(output);
            }

            foreach (var window in context.Render.Windows)
            {
                window.SetBorders(Wm.Edges.All, 6, Wm.WmColor.FromRgba(0xff, 0x00, 0x00));
                window.SetClipBox(new Wm.Rect(0, 0, 10, 10));
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;
        fixture.Host.RenderFrame();

        for (var x = 0; x < 160; x += 8)
        {
            Assert.NotEqual(Red, fixture.Host.Pixel(x, 4));
        }
    }

    [Fact]
    public void Capture_session_counts_reach_the_manager()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var output = Assert.Single(fixture.Outputs);
        var window = Assert.Single(fixture.Windows);

        Assert.Equal(0, window.CaptureSessions);
        Assert.Equal(0, output.CaptureSessions);

        fixture.Server.SetCaptureSessions(toplevel.ServerToplevel, 2);
        fixture.Server.SetCaptureSessions(fixture.Host.Output, 1);
        fixture.Settle();

        Assert.Equal(2, window.CaptureSessions);
        Assert.Equal(1, output.CaptureSessions);
    }

    [Fact]
    public void A_windows_presentation_hint_reaches_the_manager()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();
        var window = Assert.Single(fixture.Windows);

        Assert.Equal(Wm.PresentationMode.Vsync, window.PresentationHint);

        fixture.Server.SetPresentationHint(toplevel.ServerToplevel, PresentationMode.Async);
        fixture.Settle();

        Assert.Equal(Wm.PresentationMode.Async, window.PresentationHint);
    }

    [Fact]
    public void The_managers_presentation_mode_choice_reaches_the_compositor()
    {
        using var fixture = new RiverFixture();
        var output = Assert.Single(fixture.Outputs);

        Assert.Equal(PresentationMode.Vsync, fixture.Server.PresentationModeOf(fixture.Host.Output));

        fixture.OnManage = _ => output.SetPresentationMode(Wm.PresentationMode.Async);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.Equal(PresentationMode.Async, fixture.Server.PresentationModeOf(fixture.Host.Output));
    }

    [Fact]
    public void Exiting_the_session_is_the_consumers_decision()
    {
        using var fixture = new RiverFixture();
        var asked = 0;
        fixture.Server.ExitSessionRequested += () => asked++;

        fixture.Client.ExitSession();
        fixture.Settle();

        Assert.Equal(1, asked);
    }

    [Fact]
    public void Another_key_pressed_while_a_binding_is_held_stops_its_repeat()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        var stops = 0;
        var armed = false;
        fixture.OnManage = _ =>
        {
            if (armed)
            {
                return;
            }

            armed = true;
            var binding = fixture.Client.Bindings.Bind(seat, "q", Wm.Modifiers.Super);
            binding.StopRepeat += () => stops++;
            binding.Enable();
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        fixture.PressKey(SuperKey);
        Assert.True(fixture.PressKey(QKey));
        Assert.Equal(0, stops);

        fixture.PressKey(WKey);
        fixture.Settle();
        Assert.Equal(1, stops);

        fixture.ReleaseKey(WKey);
        fixture.ReleaseKey(QKey);
        fixture.ReleaseKey(SuperKey);
    }

    [Fact]
    public void A_bound_modifier_still_reaches_the_seats_own_modifier_state()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        var taps = 0;
        var chords = 0;
        var armed = false;
        fixture.OnManage = _ =>
        {
            if (armed)
            {
                return;
            }

            armed = true;

            var tap = fixture.Client.Bindings.Bind(seat, "Super_L", Wm.Modifiers.None);
            tap.Pressed += () => taps++;
            tap.Enable();

            var chord = fixture.Client.Bindings.Bind(seat, "q", Wm.Modifiers.Super);
            chord.Pressed += () => chords++;
            chord.Enable();
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        Assert.True(fixture.SendRawKey(SuperKey, pressed: true));
        Assert.Equal(1, taps);

        Assert.True(fixture.SendRawKey(QKey, pressed: true));
        Assert.Equal(1, chords);

        fixture.SendRawKey(QKey, pressed: false);
        fixture.SendRawKey(SuperKey, pressed: false);

        Assert.False(fixture.SendRawKey(QKey, pressed: true));
        Assert.Equal(1, chords);
        fixture.SendRawKey(QKey, pressed: false);
    }

    [Fact]
    public void A_version_1_manager_gets_no_stop_repeat_rather_than_a_dead_compositor()
    {
        using var fixture = new RiverFixture(bindingsCap: 1);
        var seat = Assert.Single(fixture.Seats);
        Assert.Equal(1u, fixture.Client.Bindings.Version);

        var presses = 0;
        var releases = 0;
        var armed = false;
        fixture.OnManage = _ =>
        {
            if (armed)
            {
                return;
            }

            armed = true;
            var binding = fixture.Client.Bindings.Bind(seat, "q", Wm.Modifiers.Super);
            binding.Pressed += () => presses++;
            binding.Released += () => releases++;
            binding.Enable();
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        fixture.PressKey(SuperKey);
        Assert.True(fixture.PressKey(QKey));
        Assert.Equal(1, presses);

        fixture.PressKey(WKey);
        fixture.Settle();

        fixture.ReleaseKey(WKey);
        fixture.ReleaseKey(QKey);
        fixture.ReleaseKey(SuperKey);
        fixture.Settle();

        Assert.Equal(1, releases);
        Assert.True(fixture.ManageSequences > 1);
    }

    [Fact]
    public void A_version_1_manager_gets_no_pointer_position()
    {
        using var fixture = new RiverFixture(managementCap: 1);
        Assert.Equal(1u, fixture.Client.Version);
        var seat = Assert.Single(fixture.Seats);

        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 40, 30);
        fixture.Settle();

        Assert.Equal(Wm.Point.Zero, seat.PointerPosition);
        Assert.True(fixture.ManageSequences > 0);
    }

    [Fact]
    public void A_warp_applies_before_an_operation_started_in_the_same_sequence()
    {
        using var fixture = new RiverFixture();
        var seat = Assert.Single(fixture.Seats);

        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 10, 10);
        fixture.Settle();

        Wm.PointerOperation? operation = null;
        fixture.OnManage = _ =>
        {
            if (operation is not null)
            {
                return;
            }

            operation = seat.StartPointerOperation();
            seat.WarpPointer(80, 60);
        };
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(operation);

        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 80, 60);
        fixture.Settle();
        Assert.Equal(new Wm.Point(0, 0), operation.Delta);

        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 90, 70);
        fixture.Settle();

        Assert.Equal(new Wm.Point(10, 10), operation.Delta);
    }

    [Fact]
    public void No_client_holds_pointer_focus_during_an_operation()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();
        var seat = Assert.Single(fixture.Seats);

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(60, 40);
            }

            foreach (var window in context.Render.Windows)
            {
                window.Node.SetPosition(0, 0);
            }
        };
        fixture.RequestManageAndSettle();

        var surface = fixture.Host.Scene.SurfaceAt(10, 10);
        Assert.NotNull(surface?.Surface);
        fixture.Host.Seat.Pointer.NotifyEnter(surface.Value.Surface, 10, 10);
        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 10, 10);
        fixture.Settle();
        Assert.NotNull(fixture.Host.Seat.Pointer.Focus);

        Wm.PointerOperation? operation = null;
        fixture.OnManage = _ => operation ??= seat.StartPointerOperation();
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        fixture.Server.NotifyPointerPosition(fixture.Host.Seat, 12, 12);
        fixture.Settle();
        Assert.Null(fixture.Host.Seat.Pointer.Focus);
        Assert.True(fixture.Server.HasPointerOperation(fixture.Host.Seat));
        GC.KeepAlive(operation);
    }

    [Fact]
    public void Removing_the_output_a_window_is_fullscreen_on_takes_it_out_of_fullscreen()
    {
        using var fixture = new RiverFixture();
        fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.Fullscreen(context.Outputs[0]);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;
        Assert.Equal(1, fixture.Server.DisplayedFullscreenCount);

        fixture.Server.RemoveOutput(fixture.Host.Output);
        fixture.Settle();

        Assert.Equal(0, fixture.Server.DisplayedFullscreenCount);
        Assert.Empty(fixture.Outputs);
    }

    [Fact]
    public void A_second_output_is_announced_with_a_disjoint_area()
    {
        using var fixture = new RiverFixture();
        var (_, global) = fixture.AddSecondOutput();
        fixture.Server.AddOutput(global);
        fixture.Settle();

        Assert.Equal(2, fixture.Outputs.Count);
        Assert.Equal(new Wm.Rect(0, 0, 160, 120), fixture.Outputs[0].Area);
        Assert.Equal(new Wm.Rect(160, 0, 160, 120), fixture.Outputs[1].Area);
        Assert.NotEqual(0u, fixture.Outputs[1].WlOutputName);
        Assert.NotEqual(fixture.Outputs[0].WlOutputName, fixture.Outputs[1].WlOutputName);
    }

    [Fact]
    public void A_window_fullscreens_on_the_second_output()
    {
        using var fixture = new RiverFixture();
        var (_, global) = fixture.AddSecondOutput();
        fixture.Server.AddOutput(global);
        fixture.Settle();
        fixture.MapToplevel();

        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.Fullscreen(context.Outputs[1]);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;

        Assert.Equal(1, fixture.Server.DisplayedFullscreenCount);
    }

    [Fact]
    public void Removing_one_of_two_outputs_fires_removed_and_keeps_the_other()
    {
        using var fixture = new RiverFixture();
        var (output, global) = fixture.AddSecondOutput();
        fixture.Server.AddOutput(global);
        fixture.Settle();
        Assert.Equal(2, fixture.Outputs.Count);

        var second = fixture.Outputs[1];
        var removedFired = 0;
        second.Removed += () => removedFired++;

        fixture.MapToplevel();
        fixture.OnManage = context =>
        {
            foreach (var window in context.Windows)
            {
                window.Fullscreen(second);
            }
        };
        fixture.RequestManageAndSettle();
        fixture.Settle();
        fixture.OnManage = null;
        Assert.Equal(1, fixture.Server.DisplayedFullscreenCount);

        fixture.Server.RemoveOutput(output);
        fixture.Settle();

        Assert.Equal(1, removedFired);
        Assert.True(second.IsRemoved);
        var survivor = Assert.Single(fixture.Outputs);
        Assert.Equal(new Wm.Rect(0, 0, 160, 120), survivor.Area);
        Assert.Equal(0, fixture.Server.DisplayedFullscreenCount);
    }

    [Fact]
    public void A_fullscreen_request_naming_a_removed_output_is_ignored()
    {
        using var fixture = new RiverFixture();
        var (output, global) = fixture.AddSecondOutput();
        fixture.Server.AddOutput(global);
        fixture.Settle();
        var second = fixture.Outputs[1];
        fixture.MapToplevel();
        fixture.Settle();

        var requested = false;
        fixture.OnManage = context =>
        {
            if (requested || !second.IsRemoved || context.Windows.Count == 0)
            {
                return;
            }

            requested = true;
            foreach (var window in context.Windows)
            {
                window.Fullscreen(second);
            }
        };
        fixture.Server.RemoveOutput(output);
        fixture.Settle();
        fixture.OnManage = null;

        Assert.True(requested);
        Assert.Equal(0, fixture.Server.DisplayedFullscreenCount);
    }

    [Fact]
    public void Input_devices_reach_the_manager_and_start_on_the_default_seat()
    {
        using var fixture = new RiverFixture();
        var keyboard = new object();
        fixture.Server.InputManager.AddDevice(keyboard, "virtual-keyboard", InputDeviceType.Keyboard);
        fixture.Settle();

        Assert.Equal(RiverInputManager.DefaultSeatName, fixture.Server.InputManager.SeatOf(keyboard));
        Assert.Equal([RiverInputManager.DefaultSeatName], fixture.Server.InputManager.SeatNames);
    }

    [Fact]
    public void A_hotplugged_device_can_be_configured_outside_a_manage_sequence()
    {
        using var fixture = new RiverFixture();

        var registry = fixture.Client.Display.GetRegistry();
        uint name = 0;
        uint version = 0;
        registry.Global += (_, e) =>
        {
            if (e.Interface == "river_input_manager_v1")
            {
                (name, version) = (e.Name, e.Version);
            }
        };
        fixture.Settle();
        Assert.NotEqual(0u, name);

        var input = registry.Bind<Wm.Protocol.RiverInputManagerV1>(name, version);
        Wm.Protocol.RiverInputDeviceV1? device = null;
        input.InputDevice += (_, e) => device = e.Id;

        var rates = new List<(int Rate, int Delay)>();
        fixture.Server.InputManager.RepeatInfoChanged += (_, rate, delay) => rates.Add((rate, delay));

        fixture.Server.InputManager.AddDevice(new object(), "hotplugged-keyboard", InputDeviceType.Keyboard);
        fixture.Settle();
        Assert.NotNull(device);

        device.SetRepeatInfo(50, 300);
        fixture.Settle();

        Assert.Equal([(50, 300)], rates);

        var before = fixture.ManageSequences;
        fixture.RequestManageAndSettle();
        Assert.True(fixture.ManageSequences > before);

        input.Dispose();
        registry.Dispose();
        fixture.Settle();
    }

    [Fact]
    public void Destroying_a_seat_moves_its_devices_back_to_the_default()
    {
        using var fixture = new RiverFixture();
        var input = fixture.Server.InputManager;
        var device = new object();
        input.AddDevice(device, "second-keyboard", InputDeviceType.Keyboard);

        var created = new List<string>();
        var destroyed = new List<string>();
        var assignments = new List<string>();
        input.SeatCreated += created.Add;
        input.SeatDestroyed += destroyed.Add;
        input.DeviceAssigned += (_, seat) => assignments.Add(seat);

        input.CreateSeatForTest("second");
        input.AssignForTest(device, "second");
        Assert.Equal("second", input.SeatOf(device));

        input.DestroySeatForTest(RiverInputManager.DefaultSeatName);
        Assert.Contains(RiverInputManager.DefaultSeatName, input.SeatNames);

        input.DestroySeatForTest("second");
        Assert.Equal(RiverInputManager.DefaultSeatName, input.SeatOf(device));
        Assert.Equal(["second"], created);
        Assert.Equal(["second"], destroyed);
        Assert.Equal(["second", RiverInputManager.DefaultSeatName], assignments);
    }

    [Fact]
    public void Assigning_a_device_to_a_seat_that_does_not_exist_falls_back_to_the_default()
    {
        using var fixture = new RiverFixture();
        var input = fixture.Server.InputManager;
        var device = new object();
        input.AddDevice(device, "tablet", InputDeviceType.Tablet);

        input.AssignForTest(device, "never-created");
        Assert.Equal(RiverInputManager.DefaultSeatName, input.SeatOf(device));
    }

    [Fact]
    public void A_keyboard_is_offered_to_the_manager_with_its_layout()
    {
        using var fixture = new RiverFixture();
        var keyboard = new object();
        fixture.Server.XkbConfig.AddKeyboard(keyboard, fixture.Host.Seat);
        fixture.Settle();

        Assert.True(fixture.Server.HasWindowManager);
        fixture.Server.XkbConfig.RemoveKeyboard(keyboard);
        fixture.Settle();
        Assert.True(fixture.Server.HasWindowManager);
    }

    [Fact]
    public void An_invalid_keymap_is_refused_and_leaves_the_one_in_use_alone()
    {
        using var host = new CompositorTestHost(160, 120);
        host.Seat.Keyboard.SetKeymap();
        var before = host.Seat.Keyboard.Keymap;
        Assert.NotNull(before);

        Assert.Throws<Xkb.XkbException>(() =>
        {
            using var context = Xkb.XkbContext.Create();
            using var _ = context.CreateKeymapFromString("this is not a keymap");
        });

        Assert.Same(before, host.Seat.Keyboard.Keymap);
    }

    [Fact]
    public void Every_libinput_setting_is_answered_even_when_unimplemented()
    {
        using var fixture = new RiverFixture();

        Assert.Null(fixture.Server.LibinputConfig.Configuration);

        var configuration = new PartialInputConfiguration();
        fixture.Server.LibinputConfig.Configuration = configuration;
        fixture.Settle();

        Assert.Equal(
            Capabilities.InputSettingResult.Success,
            configuration.Set(1, Capabilities.InputSetting.Tap, new Capabilities.InputSettingValue(1)));
        Assert.Equal(
            Capabilities.InputSettingResult.Unsupported,
            configuration.Set(1, Capabilities.InputSetting.Rotation, new Capabilities.InputSettingValue(90)));
        Assert.Equal(
            [Capabilities.InputSetting.Tap, Capabilities.InputSetting.Rotation],
            configuration.Seen);

        fixture.Server.LibinputConfig.RemoveDevice(1);
        fixture.Settle();
    }

    private sealed class PartialInputConfiguration : Capabilities.IInputDeviceConfiguration
    {
        public event Action<Capabilities.InputDeviceInfo>? DeviceAdded
        {
            add { }
            remove { }
        }

        public event Action<ulong>? DeviceRemoved
        {
            add { }
            remove { }
        }

        public List<Capabilities.InputSetting> Seen { get; } = [];

        public int Enumerate(Span<Capabilities.InputDeviceInfo> devices)
        {
            if (devices.Length < 1)
            {
                return -1;
            }

            devices[0] = new Capabilities.InputDeviceInfo(1, "touchpad", Capabilities.InputDeviceCapability.Pointer, null);
            return 1;
        }

        public bool TryGet(ulong deviceId, Capabilities.InputSetting setting, out Capabilities.InputSettingValue value)
        {
            value = default;
            return false;
        }

        public Capabilities.InputSettingResult Set(ulong deviceId, Capabilities.InputSetting setting, in Capabilities.InputSettingValue value)
        {
            Seen.Add(setting);
            return setting == Capabilities.InputSetting.Tap
                ? Capabilities.InputSettingResult.Success
                : Capabilities.InputSettingResult.Unsupported;
        }
    }

    private const uint WKey = 17;

    private const uint Green = 0xff00ff00;

    private const uint Red = 0xffff0000;

    [Fact]
    public void A_window_frozen_mid_resize_keeps_its_decorations_on_screen()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context => context.Windows[0].ProposeDimensions(50, 50);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var surface = fixture.Client.Compositor!.CreateSurface();
        Wm.WmDecoration? decoration = null;
        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => decoration ??= window.CreateDecorationAbove(surface);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(decoration);

        var buffer = fixture.CreateManagerBuffer(30, 10, Green);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 10);
        surface.Commit();
        fixture.Settle();
        fixture.DropStrayHostScenes();

        fixture.Host.RenderFrame();
        Assert.Equal(Green, fixture.Host.Pixel(5, 5));

        fixture.OnManage = context => context.Windows[0].ProposeDimensions(80, 60);
        fixture.RunManageLeavingClientsUnanswered();
        fixture.OnManage = null;
        Assert.Equal(1, fixture.Server.FrozenWindowCount);

        fixture.Host.RenderFrame();
        Assert.Equal(Green, fixture.Host.Pixel(5, 5));

        fixture.Settle();
        decoration.Dispose();
        surface.Destroy();
        fixture.Settle();
        GC.KeepAlive(toplevel);
    }

    [Fact]
    public void A_decorations_held_commit_reaches_the_screen_with_the_render_sequence()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        fixture.OnManage = context => context.Windows[0].ProposeDimensions(50, 50);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;

        var surface = fixture.Client.Compositor!.CreateSurface();
        Surface? decoSurface = null;
        fixture.Host.Compositor.SurfaceCreated += s => decoSurface = s;
        Wm.WmDecoration? decoration = null;
        var window = Assert.Single(fixture.Windows);
        fixture.OnManage = _ => decoration ??= window.CreateDecorationAbove(surface);
        fixture.RequestManageAndSettle();
        fixture.OnManage = null;
        Assert.NotNull(decoration);

        var green = fixture.CreateManagerBuffer(30, 10, Green);
        surface.Attach(green.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 10);
        surface.Commit();
        fixture.Settle();
        fixture.DropStrayHostScenes();

        fixture.Host.RenderFrame();
        Assert.Equal(Green, fixture.Host.Pixel(5, 5));

        var red = fixture.CreateManagerBuffer(30, 10, Red);
        var seenMidSequence = 0u;
        fixture.OnRender = _ =>
        {
            if (seenMidSequence != 0)
            {
                return;
            }

            decoration.SyncNextCommit();
            surface.Attach(red.Proxy, 0, 0);
            surface.Damage(0, 0, 30, 10);
            surface.Commit();

            fixture.PumpServerOnly();
            fixture.Host.RenderFrame();
            Assert.True(decoSurface?.HasParkedCommits, "the decoration's commit reached the compositor unparked");
            seenMidSequence = fixture.Host.Pixel(5, 5);
        };
        fixture.RequestManageAndSettle();
        fixture.OnRender = null;

        Assert.Equal(Green, seenMidSequence);

        fixture.Host.RenderFrame();
        Assert.Equal(Red, fixture.Host.Pixel(5, 5));

        decoration.Dispose();
        surface.Destroy();
        fixture.Settle();
        GC.KeepAlive(toplevel);
    }

    [Fact]
    public void A_manager_that_disconnects_leaves_every_client_alive()
    {
        using var fixture = new RiverFixture();
        var toplevel = fixture.MapToplevel();

        var lost = false;
        fixture.Server.WindowManagerLost += () => lost = true;

        fixture.DropManager();

        Assert.True(lost);
        Assert.False(fixture.Server.HasWindowManager);
        Assert.True(toplevel.ServerToplevel.IsMapped);

        using var replacement = fixture.ConnectManager();
        var readopted = new List<string?>();
        replacement.Manage += context =>
        {
            foreach (var window in context.NewWindows)
            {
                readopted.Add(window.AppId);
            }
        };
        fixture.Settle();

        Assert.Contains("basin-test", readopted);
    }
}

internal sealed class RiverFixture : IDisposable
{
    private readonly List<Wm.RiverWindowManager> _managers = [];
    private readonly SceneTree _windowTree;
    private readonly List<int> _clientFds = [];

    public RiverFixture(
        int width = 160,
        int height = 120,
        uint managementCap = uint.MaxValue,
        uint bindingsCap = uint.MaxValue)
    {
        Host = new CompositorTestHost(width, height);
        _windowTree = new SceneTree(Host.Scene.Root);
        Server = new RiverWindowManager(
            Host.Display, Host.Loop, Host.Scene, _windowTree, Host.Shell, Host.Layout, [Host.Seat]);
        Server.Compositor = Host.Compositor;
        Server.Decorations = Host.Decorations;
        Server.AddOutput(Host.OutputGlobal);

        Host.Seat.Keyboard.SetKeymap();

        Client = ConnectManager(managementCap, bindingsCap);
        Client.Manage += context =>
        {
            ManageSequences++;
            Windows = [.. context.Windows];
            Outputs = [.. context.Outputs];
            Seats = [.. context.Seats];
            OnManage?.Invoke(context);
        };
        Client.Render += context =>
        {
            RenderSequences++;
            OnRender?.Invoke(context);
        };
        Settle();
    }

    public CompositorTestHost Host { get; }

    public RiverWindowManager Server { get; }

    public Wm.RiverWindowManager Client { get; }

    public int ManageSequences { get; private set; }

    public int RenderSequences { get; private set; }

    public IReadOnlyList<Wm.WmWindow> Windows { get; private set; } = [];

    public IReadOnlyList<Wm.WmOutput> Outputs { get; private set; } = [];

    public IReadOnlyList<Wm.WmSeat> Seats { get; private set; } = [];

    public Action<Wm.ManageContext>? OnManage { get; set; }

    public Action<Wm.RenderContext>? OnRender { get; set; }

    public int SyncViolations { get; private set; }

    public (HeadlessOutput Output, OutputGlobal Global) AddSecondOutput(int width = 160, int height = 120)
    {
        var mode = new OutputMode(width, height, 60_000);
        var output = Host.Backend.CreateOutput(mode, manualFrameClock: true);
        using var state = new OutputState();
        output.Commit(state.SetEnabled(true).SetMode(mode));
        var global = new OutputGlobal(Host.Display, output);
        Host.Layout.Add(output, Host.Output.CurrentMode.Width, 0);
        Host.TrackOutputGlobal(output, global);
        _extraOutputs.Add((output, global));
        return (output, global);
    }

    private readonly List<(HeadlessOutput Output, OutputGlobal Global)> _extraOutputs = [];

    public Wm.RiverWindowManager ConnectManager(uint managementCap = uint.MaxValue, uint bindingsCap = uint.MaxValue)
    {
        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (socketpair(AF_UNIX, SOCK_STREAM, 0, fds) != 0)
            {
                throw new InvalidOperationException("socketpair failed");
            }

            serverFd = fds[0];
            clientFd = fds[1];
        }

        Host.Display.CreateClient(serverFd);
        _clientFds.Add(clientFd);

        var manager = new Wm.RiverWindowManager(clientFd, PumpServer, managementCap, bindingsCap);
        _managers.Add(manager);
        return manager;
    }

    public MappedToplevel MapToplevel(
        int width = 40, int height = 40, Action<Basin.Shell.Xdg.Protocol.XdgToplevel>? beforeMap = null)
    {
        var toplevel = MappedToplevel.Map(Host, Host.Client, width, height, beforeMap: beforeMap);
        toplevel.XdgSurface.Configure += (_, _) => Repaint(toplevel);

        DropStrayHostScenes();
        Settle();
        return toplevel;
    }

    private void Repaint(MappedToplevel toplevel)
    {
        var width = toplevel.ConfiguredWidth > 0 ? toplevel.ConfiguredWidth : 40;
        var height = toplevel.ConfiguredHeight > 0 ? toplevel.ConfiguredHeight : 40;
        var buffer = Host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xff336699));
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
    }

    public ClientShmBuffer CreateManagerBuffer(int width, int height, uint color)
    {
        var buffer = new ClientShmBuffer(Client.Shm!, width, height);
        Fill.Solid(width, height, color)(buffer.Data, buffer.Stride);
        _managerBuffers.Add(buffer);
        return buffer;
    }

    private readonly List<ClientShmBuffer> _managerBuffers = [];

    public void PaintToplevel(MappedToplevel toplevel, int width, int height)
    {
        var buffer = Host.Client.CreateBuffer(width, height, Fill.Solid(width, height, 0xff336699));
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
        Host.PumpToServer();
    }

    public void RequestManageAndSettle()
    {
        Client.RequestManage();
        Settle();
    }

    public void DropStrayHostScenes()
    {
        foreach (var stray in Host.SurfaceScenes.ToArray())
        {
            stray.Destroy();
        }

        Host.SurfaceScenes.Clear();
    }

    public void RunManageLeavingClientsUnanswered(int rounds = 8)
    {
        Client.RequestManage();
        for (var i = 0; i < rounds; i++)
        {
            foreach (var manager in _managers)
            {
                Flush(manager);
            }

            Host.Loop.Dispatch(0);

            foreach (var manager in _managers)
            {
                Dispatch(manager);
            }
        }
    }

    public void PumpServerOnly()
    {
        foreach (var manager in _managers)
        {
            Flush(manager);
        }

        Host.Loop.Dispatch(0);
    }

    public void Settle(int rounds = 24)
    {
        for (var i = 0; i < rounds; i++)
        {
            foreach (var manager in _managers)
            {
                Flush(manager);
            }

            PumpServer();

            foreach (var manager in _managers)
            {
                Dispatch(manager);
            }
        }
    }

    private void PumpServer()
    {
        Host.Loop.Dispatch(0);
        Host.PumpToClient();
    }

    public bool PressKey(uint key) => SendKey(key, pressed: true);

    public bool ReleaseKey(uint key) => SendKey(key, pressed: false);

    public bool SendRawKey(uint key, bool pressed)
    {
        var claimed = Server.HandleKey(Host.Seat, key, pressed);
        if (!claimed)
        {
            Host.Seat.Keyboard.NotifyKey(
                0,
                key,
                pressed ? Wayland.WlKeyboard.KeyState.Pressed : Wayland.WlKeyboard.KeyState.Released);
        }

        Server.NotifyModifiers(Host.Seat);
        Settle(4);
        return claimed;
    }

    private bool SendKey(uint key, bool pressed)
    {
        var keyboard = Host.Seat.Keyboard;
        if (keyboard.State is { } state)
        {
            state.UpdateKey(key + 8, pressed ? Xkb.XkbKeyDirection.Down : Xkb.XkbKeyDirection.Up);
            keyboard.NotifyModifiers(
                state.SerializeMods(Xkb.XkbStateComponent.ModsDepressed),
                state.SerializeMods(Xkb.XkbStateComponent.ModsLatched),
                state.SerializeMods(Xkb.XkbStateComponent.ModsLocked),
                state.SerializeLayout(Xkb.XkbStateComponent.LayoutEffective));
        }

        Server.NotifyModifiers(Host.Seat);
        var claimed = Server.HandleKey(Host.Seat, key, pressed);
        if (!claimed)
        {
            keyboard.NotifyKey(
                0,
                key,
                pressed ? Wayland.WlKeyboard.KeyState.Pressed : Wayland.WlKeyboard.KeyState.Released);
        }

        Settle(4);
        return claimed;
    }

    public void DropManager()
    {
        Client.Dispose();
        _managers.Remove(Client);
        Settle();
    }

    public void Dispose()
    {
        foreach (var buffer in _managerBuffers)
        {
            buffer.Dispose();
        }

        _managerBuffers.Clear();
        foreach (var manager in _managers)
        {
            manager.Dispose();
        }

        _managers.Clear();
        Server.Dispose();
        _windowTree.Destroy();
        foreach (var (output, global) in _extraOutputs)
        {
            global.Dispose();
            output.Destroy();
        }

        _extraOutputs.Clear();
        Host.Dispose();
    }

    private static void Flush(Wm.RiverWindowManager manager)
    {
        try
        {
            manager.Display.Flush();
        }
        catch (Wayland.WaylandException)
        {
        }
    }

    private void Dispatch(Wm.RiverWindowManager manager)
    {
        try
        {
            manager.DispatchPending();
        }
        catch (Wayland.WaylandException)
        {
        }
        catch (InvalidOperationException)
        {
            SyncViolations++;
        }
    }

    private const int AF_UNIX = 1;
    private const int SOCK_STREAM = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* sv);
}
