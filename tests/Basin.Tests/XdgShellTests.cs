using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class XdgShellTests
{
    [Fact]
    public void Toplevel_maps_through_the_configure_contract()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        Assert.True(window.ServerToplevel.IsMapped);
        Assert.Equal("basin-test", window.ServerToplevel.AppId);
        Assert.NotEqual(0u, window.LastConfigureSerial);
    }

    [Fact]
    public void The_fullscreen_request_carries_its_output()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        window.Toplevel.SetFullscreen(host.Client.Outputs[0]);
        host.PumpToServer();
        Assert.True(window.ServerToplevel.RequestedFullscreen);
        Assert.Same(host.Output, window.ServerToplevel.RequestedFullscreenOutput);

        window.Toplevel.UnsetFullscreen();
        host.PumpToServer();
        Assert.False(window.ServerToplevel.RequestedFullscreen);
        Assert.Null(window.ServerToplevel.RequestedFullscreenOutput);

        window.Toplevel.SetFullscreen(null);
        host.PumpToServer();
        Assert.True(window.ServerToplevel.RequestedFullscreen);
        Assert.Null(window.ServerToplevel.RequestedFullscreenOutput);
    }

    [Fact]
    public void Set_size_and_states_reach_the_client_in_one_configure()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        window.ServerToplevel.SetActivated(true);
        window.ServerToplevel.SetSize(300, 200);
        host.PumpUntil(() => window.ConfiguredWidth == 300 && window.ConfiguredHeight == 200);
        Assert.True(window.ServerToplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Activated));
    }

    [Fact]
    public void Move_request_requires_a_still_held_button()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var pointer = client.Seat!.GetPointer();
        uint pressSerial = 0;
        pointer.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();

        var moves = 0;
        window.ServerToplevel.MoveRequested += _ => moves++;

        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 3, 3);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => pressSerial != 0);

        window.Toplevel.Move(client.Seat, pressSerial);
        host.PumpToServer();
        Assert.Equal(1, moves);

        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        window.Toplevel.Move(client.Seat, pressSerial);
        host.PumpToServer();
        Assert.Equal(1, moves);
    }

    [Fact]
    public void Hiding_by_role_destroy_unmaps_and_the_wl_surface_can_be_reshown()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var window = MappedToplevel.Map(host, client);

        var first = window.ServerToplevel.Xdg;
        var firstUnmapped = false;
        var firstResurrections = 0;
        first.Unmapped += () => firstUnmapped = true;
        first.Mapped += () => firstResurrections++;

        window.Toplevel.Destroy();
        window.XdgSurface.Destroy();
        host.PumpToServer();
        Assert.True(firstUnmapped);
        Assert.False(first.IsMapped);

        window.Surface.Attach(null, 0, 0);
        window.Surface.Commit();
        host.PumpToServer();

        XdgToplevelWindow? reborn = null;
        host.Shell.NewToplevel += t => reborn ??= t;
        var xdgSurface = client.WmBase!.GetXdgSurface(window.Surface);
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };
        xdgSurface.GetToplevel();
        window.Surface.Attach(null, 0, 0);
        window.Surface.Commit();
        host.PumpUntil(() => configured);
        window.Surface.Attach(window.Buffer.Proxy, 0, 0);
        window.Surface.Commit();
        host.PumpUntil(() => reborn is { IsMapped: true });

        Assert.NotSame(first, reborn!.Xdg);
        Assert.Equal(0, firstResurrections);
    }

    [Fact]
    public void Window_geometry_latches_on_commit()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        window.XdgSurface.SetWindowGeometry(5, 6, 40, 30);
        host.PumpToServer();
        Assert.NotEqual(new Box(5, 6, 40, 30), window.ServerToplevel.Xdg.WindowGeometry);

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal(new Box(5, 6, 40, 30), window.ServerToplevel.Xdg.WindowGeometry);
    }

    [Fact]
    public void A_later_configure_carries_the_size_the_client_settled_on()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);

        window.ServerToplevel.SetSize(300, 200);
        host.PumpUntil(() => window.ConfiguredWidth == 300 && window.ConfiguredHeight == 200);

        window.XdgSurface.SetWindowGeometry(0, 0, 240, 200);
        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal(new Box(0, 0, 240, 200), window.ServerToplevel.Xdg.WindowGeometry);

        var serial = window.LastConfigureSerial;
        window.ServerToplevel.SetResizing(false);
        window.ServerToplevel.SetActivated(true);
        host.PumpUntil(() => window.LastConfigureSerial != serial);

        Assert.Equal(240, window.ConfiguredWidth);
        Assert.Equal(200, window.ConfiguredHeight);
    }

    [Fact]
    public void Close_reaches_the_client()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        window.ServerToplevel.Close();
        host.PumpUntil(() => window.CloseReceived);
    }

    [Fact]
    public void Ack_of_any_sent_serial_survives_a_deep_configure_backlog()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;

        XdgToplevelWindow? serverToplevel = null;
        host.Shell.NewToplevel += t => serverToplevel ??= t;

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        _ = xdgSurface.GetToplevel();
        var serials = new List<uint>();
        xdgSurface.Configure += (_, e) => serials.Add(e.Serial);
        surface.Commit();
        host.PumpUntil(() => serials.Count == 1 && serverToplevel is not null);

        for (var i = 0; serials.Count < 80; i++)
        {
            var next = serials.Count + 1;
            serverToplevel!.SetSize(100 + i, 100);
            host.PumpUntil(() => serials.Count >= next);
        }

        Assert.True(serverToplevel!.Xdg.HasUnackedConfigure);

        xdgSurface.AckConfigure(serials[0]);
        host.PumpToServer();
        Assert.True(serverToplevel.Xdg.HasUnackedConfigure);

        xdgSurface.AckConfigure(serials[^1]);
        host.PumpToServer();
        Assert.False(serverToplevel.Xdg.HasUnackedConfigure);

        var delivered = serials.Count;
        serverToplevel.SetSize(500, 400);
        host.PumpUntil(() => serials.Count > delivered);
    }

    [Fact]
    public void Buffer_before_initial_configure_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        _ = xdgSurface.GetToplevel();
        var buffer = client.CreateBuffer(8, 8, Fill.Solid(8, 8, 0xFF000000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            client.Display.Dispatch();
            client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Popup_positions_and_dismisses_on_click_outside()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client, 100, 80);

        var pointer = client.Seat!.GetPointer();
        uint pressSerial = 0;
        pointer.Button += (_, e) =>
        {
            if (e.State == WlPointer.ButtonState.Pressed)
            {
                pressSerial = e.Serial;
            }
        };
        host.PumpToClient();
        host.Seat.Pointer.NotifyEnter(parent.ServerSurface, 15, 15);
        host.Seat.Pointer.NotifyButton(1, 0x110, WlPointer.ButtonState.Pressed);
        host.Seat.Pointer.NotifyButton(2, 0x110, WlPointer.ButtonState.Released);
        host.PumpUntil(() => pressSerial != 0);

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(50, 40);
        positioner.SetAnchorRect(10, 10, 20, 20);
        positioner.SetAnchor(Basin.Shell.Xdg.Protocol.XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(Basin.Shell.Xdg.Protocol.XdgPositioner.Gravity.BottomRight);

        XdgPopupWindow? serverPopup = null;
        host.Shell.NewPopup += popup => serverPopup = popup;

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popup = popupXdg.GetPopup(parent.XdgSurface, positioner);
        popup.Grab(client.Seat, pressSerial);
        var popupDone = false;
        popup.PopupDone += (_, _) => popupDone = true;
        (int X, int Y)? configured = null;
        popup.Configure += (_, e) => configured = (e.X, e.Y);
        popupXdg.Configure += (_, e) => popupXdg.AckConfigure(e.Serial);
        popupSurface.Commit();
        host.PumpUntil(() => configured is not null);

        Assert.Equal((30, 30), configured!.Value);
        Assert.NotNull(serverPopup);
        Assert.True(serverPopup!.HasGrab);

        var popupBuffer = client.CreateBuffer(50, 40, Fill.Solid(50, 40, 0xFF444444));
        popupSurface.Attach(popupBuffer.Proxy, 0, 0);
        popupSurface.Commit();
        host.PumpToServer();

        host.Seat.Pointer.NotifyClearFocus();
        host.Seat.Pointer.NotifyButton(3, 0x110, WlPointer.ButtonState.Pressed);
        host.PumpUntil(() => popupDone);
    }

    [Fact]
    public void Popup_surface_origin_backs_out_the_popups_own_geometry_offset()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client, 100, 80);

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(50, 40);
        positioner.SetAnchorRect(10, 10, 20, 20);
        positioner.SetAnchor(Basin.Shell.Xdg.Protocol.XdgPositioner.Anchor.BottomRight);
        positioner.SetGravity(Basin.Shell.Xdg.Protocol.XdgPositioner.Gravity.BottomRight);

        XdgPopupWindow? serverPopup = null;
        host.Shell.NewPopup += popup => serverPopup = popup;

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        _ = popupXdg.GetPopup(parent.XdgSurface, positioner);
        popupXdg.Configure += (_, e) => popupXdg.AckConfigure(e.Serial);

        popupXdg.SetWindowGeometry(12, 12, 50, 40);
        popupSurface.Commit();
        host.PumpUntil(() => serverPopup is not null && !serverPopup.Xdg.WindowGeometry.IsEmpty);

        Assert.Equal(new Box(30, 30, 50, 40), serverPopup!.Geometry);
        Assert.Equal(new Point(18, 18), serverPopup.SurfacePosition);
    }

    [Fact]
    public void Scene_frame_done_walk_reaches_popup_surfaces()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client, 100, 80);

        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(50, 40);
        positioner.SetAnchorRect(10, 10, 20, 20);

        var popupSurface = client.Compositor.CreateSurface();
        var popupXdg = client.WmBase.GetXdgSurface(popupSurface);
        var popup = popupXdg.GetPopup(parent.XdgSurface, positioner);
        var configured = false;
        popupXdg.Configure += (_, e) =>
        {
            popupXdg.AckConfigure(e.Serial);
            configured = true;
        };
        popupSurface.Commit();
        host.PumpUntil(() => configured);

        var popupBuffer = client.CreateBuffer(50, 40, Fill.Solid(50, 40, 0xFF444444));
        var frameDone = false;
        var callback = popupSurface.Frame();
        callback.Done += (_, _) => frameDone = true;
        popupSurface.Attach(popupBuffer.Proxy, 0, 0);
        popupSurface.Commit();
        host.PumpToServer();
        Assert.False(frameDone);

        host.Scene.SendFrameDone(1);
        host.PumpUntil(() => frameDone);
        _ = popup;
    }

    [Fact]
    public void Suspended_and_constrained_reach_a_v7_client_and_never_an_older_one()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;

        var legacyBase = client.BindAt<Basin.Shell.Xdg.Protocol.XdgWmBase>("xdg_wm_base", 5);
        legacyBase.Ping += (_, e) => legacyBase.Pong(e.Serial);
        host.PumpToServer();

        var modern = MappedToplevel.Map(host, client);
        var legacy = MappedToplevel.Map(host, client, wmBase: legacyBase);

        foreach (var window in new[] { modern, legacy })
        {
            window.ServerToplevel.SetSuspended(true);
            window.ServerToplevel.SetConstrained(ResizeEdges.Left | ResizeEdges.Top);
        }

        const uint Suspended = 9;
        const uint ConstrainedLeft = 10;
        const uint ConstrainedTop = 12;
        host.PumpUntil(() => modern.ConfiguredStates.Contains(Suspended));

        Assert.Contains(ConstrainedLeft, modern.ConfiguredStates);
        Assert.Contains(ConstrainedTop, modern.ConfiguredStates);

        Assert.True(legacy.ServerToplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Suspended));
        Assert.DoesNotContain(Suspended, legacy.ConfiguredStates);
        Assert.DoesNotContain(ConstrainedLeft, legacy.ConfiguredStates);
        Assert.DoesNotContain(ConstrainedTop, legacy.ConfiguredStates);
    }
}
