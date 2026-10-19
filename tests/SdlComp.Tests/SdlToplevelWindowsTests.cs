using Basin.Sdl;
using Basin.Shell.Xdg.Protocol;
using SDL3;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlToplevelWindowsTests
{
    private sealed class Client
    {
        public Wayland.WlSurface Surface = null!;
        public XdgSurface Xdg = null!;
        public XdgToplevel Toplevel = null!;
        public bool Configured;
        public bool CloseReceived;
        public List<(int Width, int Height)> Sizes = [];
    }

    private static Client Map(SdlCompHarness harness, int width, int height, string title, Action<Client>? before = null)
    {
        var client = harness.Client;
        var mapped = new Client { Surface = client.Compositor.CreateSurface() };
        mapped.Xdg = client.WmBase!.GetXdgSurface(mapped.Surface);
        mapped.Toplevel = mapped.Xdg.GetToplevel();
        mapped.Toplevel.SetTitle(title);
        mapped.Toplevel.SetAppId("org.basin.sdl");
        mapped.Xdg.Configure += (_, e) =>
        {
            mapped.Xdg.AckConfigure(e.Serial);
            mapped.Configured = true;
        };
        mapped.Toplevel.Configure += (_, e) => mapped.Sizes.Add((e.Width, e.Height));
        mapped.Toplevel.Close += (_, _) => mapped.CloseReceived = true;
        before?.Invoke(mapped);
        mapped.Surface.Commit();
        harness.PumpUntil(() => mapped.Configured, "the toplevel was never configured");
        var buffer = client.CreateBuffer(width, height, (data, stride) => { });
        mapped.Surface.Attach(buffer.Proxy, 0, 0);
        mapped.Surface.Damage(0, 0, width, height);
        mapped.Surface.Commit();
        return mapped;
    }

    [Fact]
    public void A_toplevel_opens_its_own_window_sized_and_titled_after_it()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        Assert.Empty(harness.Comp.FrameLoop.Windows);
        var mapped = Map(harness, 200, 150, "first");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;
        Assert.Equal((200, 150), window.Window.PixelSize);
        Assert.Equal("first", SDL.GetWindowTitle(window.Window.Handle));
        Assert.Contains(window, harness.Comp.FrameLoop.Windows);

        mapped.Toplevel.SetTitle("renamed");
        mapped.Surface.Commit();
        harness.PumpUntil(() => SDL.GetWindowTitle(window.Window.Handle) == "renamed", "the title never followed");

        var second = Map(harness, 120, 90, "second");
        harness.PumpUntil(() => toplevels.Entries.Count(e => e.Window is not null) == 2, "the second window never opened");
        Assert.Equal(2, harness.Comp.FrameLoop.Windows.Count);
    }

    [Fact]
    public void A_window_resize_configures_the_toplevel_and_a_close_reaches_it()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var mapped = Map(harness, 200, 150, "sized");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;

        Assert.True(SDL.SetWindowSize(window.Window.Handle, 320, 240));
        harness.PumpUntil(() => mapped.Sizes.Contains((320, 240)), "the resize never reached the client");
        Assert.Equal((320, 240), window.View.Output.CurrentMode is var mode ? (mode.Width, mode.Height) : default);

        SdlEvents.Window(window.Window.Id, SDL.EventType.WindowCloseRequested);
        harness.PumpUntil(() => mapped.CloseReceived, "xdg_toplevel.close never arrived");
        mapped.Toplevel.Dispose();
        mapped.Xdg.Dispose();
        mapped.Surface.Dispose();
        harness.PumpUntil(() => toplevels.Entries.Count == 0, "the window outlived its toplevel");
        Assert.Empty(harness.Comp.FrameLoop.Windows);
        Assert.False(harness.Comp.Loop.QuitRequested);
    }

    [Fact]
    public void The_window_model_resizes_the_window_and_reports_its_geometry()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var mapped = Map(harness, 200, 150, "modelled");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;
        var model = harness.Host.Services.Require<Basin.Capabilities.IToplevelModel>();
        var infos = new Basin.Capabilities.ToplevelInfo[4];
        Assert.Equal(1, model.Enumerate(infos));
        var id = infos[0].Id;
        harness.PumpUntil(() => model.TryGet(id, out var info) && info.Geometry == new Basin.Box(0, 0, 200, 150), "the geometry was never reported");

        Assert.True(model.Request(id, new Basin.Capabilities.ToplevelRequest(
            Basin.Capabilities.ToplevelRequestKind.Resize, Geometry: new Basin.Box(0, 0, 300, 220))));
        harness.PumpUntil(() => window.Window.PixelSize == (300, 220), "the window never took the requested size");
        harness.PumpUntil(() => mapped.Sizes.Contains((300, 220)), "the resize never reached the client");
        Assert.Equal(toplevels.CanMoveWindows, model.Request(id, new Basin.Capabilities.ToplevelRequest(
            Basin.Capabilities.ToplevelRequestKind.Move, Geometry: new Basin.Box(40, 30, 0, 0))));
    }

    [Fact]
    public void A_client_that_resizes_itself_resizes_its_window()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var mapped = Map(harness, 200, 150, "grows");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;
        var bigger = harness.Client.CreateBuffer(260, 180, (data, stride) => { });
        mapped.Surface.Attach(bigger.Proxy, 0, 0);
        mapped.Surface.Damage(0, 0, 260, 180);
        mapped.Surface.Commit();
        harness.PumpUntil(() => window.Window.PixelSize == (260, 180), "the window never followed the client's size");
    }

    [Fact]
    public void A_client_that_draws_its_own_frame_gets_a_borderless_window_over_its_whole_surface()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var mapped = Map(harness, 240, 190, "csd", client => client.Xdg.SetWindowGeometry(20, 16, 200, 150));
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var entry = toplevels.Entries.Single();
        var window = entry.Window!;

        Assert.True(entry.ClientDecorated);
        Assert.True((SDL.GetWindowFlags(window.Window.Handle) & SDL.WindowFlags.Borderless) != 0);
        Assert.Equal((240, 190), window.Window.PixelSize);
        Assert.Equal(0, window.View.SceneOutput.Position.X);

        Assert.True(SDL.SetWindowSize(window.Window.Handle, 320, 240));
        harness.PumpUntil(() => mapped.Sizes.Contains((280, 200)), "the resize never reached the client without its margins");
    }

    [Fact]
    public void A_client_that_asks_for_server_side_decorations_gets_a_bordered_window_at_its_geometry()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        Map(harness, 240, 190, "ssd", client =>
        {
            client.Xdg.SetWindowGeometry(20, 16, 200, 150);
            harness.Client.DecorationManager!.GetToplevelDecoration(client.Toplevel)
                .SetMode(Basin.Shell.Xdg.Protocol.ZxdgToplevelDecorationV1.Mode.ServerSide);
        });
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var entry = toplevels.Entries.Single();
        var window = entry.Window!;

        Assert.False(entry.ClientDecorated);
        Assert.True((SDL.GetWindowFlags(window.Window.Handle) & SDL.WindowFlags.Borderless) == 0);
        Assert.Equal((200, 150), window.Window.PixelSize);
        Assert.Equal(20, window.View.SceneOutput.Position.X);
    }

    [Fact]
    public void Pointer_and_keys_in_a_window_reach_its_toplevel()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var pointer = harness.Client.Seat!.GetPointer();
        var keyboard = harness.Client.Seat!.GetKeyboard();
        var entered = (X: -1.0, Y: -1.0);
        var keys = new List<uint>();
        pointer.Enter += (_, e) => entered = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        pointer.Motion += (_, e) => entered = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        keyboard.Key += (_, e) => keys.Add(e.Key);
        _ = Map(harness, 200, 150, "a");
        var second = Map(harness, 200, 150, "b");
        harness.PumpUntil(() => toplevels.Entries.Count(e => e.Window is not null) == 2, "the windows never opened");
        var target = toplevels.Entries.Single(e => e.Toplevel.Title == "b").Window!;

        SdlEvents.Motion(target.Window.Id, 30, 40);
        harness.PumpUntil(() => entered == (30.0, 40.0), "the pointer never reached the second toplevel at its point");
        Assert.Same(toplevels.Entries.Single(e => e.Toplevel.Title == "b").Toplevel.Surface, harness.Host.Seat.Pointer.Focus);

        SdlEvents.Window(target.Window.Id, SDL.EventType.WindowFocusGained);
        SdlEvents.Key(target.Window.Id, SDL.Scancode.B, true);
        harness.PumpUntil(() => keys.Contains(48u), "the key never reached the toplevel");
        Assert.Same(toplevels.Entries.Single(e => e.Toplevel.Title == "b").Toplevel.Surface, harness.Host.Seat.Keyboard.Focus);
    }

    private static (XdgPopup Popup, Wayland.WlSurface Surface) OpenPopup(SdlCompHarness harness, Client parent, int x, int y, int width, int height, uint? grabSerial)
    {
        var client = harness.Client;
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(width, height);
        positioner.SetAnchorRect(x, y, 1, 1);
        positioner.SetAnchor(XdgPositioner.Anchor.TopLeft);
        positioner.SetGravity(XdgPositioner.Gravity.BottomRight);
        var surface = client.Compositor.CreateSurface();
        var xdg = client.WmBase.GetXdgSurface(surface);
        var popup = xdg.GetPopup(parent.Xdg, positioner);
        var configured = false;
        xdg.Configure += (_, e) =>
        {
            xdg.AckConfigure(e.Serial);
            configured = true;
        };
        if (grabSerial is { } serial)
        {
            popup.Grab(client.Seat!, serial);
        }

        surface.Commit();
        harness.PumpUntil(() => configured, "the popup was never configured");
        var buffer = client.CreateBuffer(width, height, (data, stride) => { });
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, width, height);
        surface.Commit();
        return (popup, surface);
    }

    [Fact]
    public void A_grabbing_popup_is_an_SDL_popup_menu_at_its_offset_and_goes_away_with_it()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var pointer = harness.Client.Seat!.GetPointer();
        uint press = 0;
        pointer.Button += (_, e) => press = e.Serial;
        var parent = Map(harness, 300, 200, "menu owner");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;
        SdlEvents.Motion(window.Window.Id, 50, 50);
        SdlEvents.Button(window.Window.Id, 1, true, 50, 50);
        harness.PumpUntil(() => press != 0, "the press never reached the client");

        var (popup, surface) = OpenPopup(harness, parent, 40, 30, 120, 80, press);
        harness.PumpUntil(() => toplevels.Popups.Any(e => e.Window is not null), "the popup window never opened");
        var popupWindow = toplevels.Popups.Single().Window!;
        var flags = SDL.GetWindowFlags(popupWindow.Window.Handle);
        if (popupWindow.Window.IsPopup)
        {
            Assert.True((flags & SDL.WindowFlags.PopupMenu) != 0, $"flags {flags}");
            Assert.Same(window.Window, popupWindow.Window.Parent);
        }
        else
        {
            Assert.True((flags & SDL.WindowFlags.Borderless) != 0, $"flags {flags}");
        }

        Assert.Equal((120, 80), popupWindow.Window.PixelSize);
        Assert.Contains(popupWindow, harness.Comp.FrameLoop.Windows);

        var entered = (X: -1.0, Y: -1.0);
        Wayland.WlSurface? over = null;
        pointer.Enter += (_, e) => (over, entered) = (e.Surface, (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble()));
        pointer.Motion += (_, e) => entered = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        SdlEvents.Motion(popupWindow.Window.Id, 10, 12);
        harness.PumpUntil(() => entered == (10.0, 12.0), "the pointer never reached the popup at its point");

        popup.Dispose();
        surface.Dispose();
        harness.PumpUntil(() => toplevels.Popups.Count == 0, "the popup window outlived the popup");
        Assert.DoesNotContain(popupWindow, harness.Comp.FrameLoop.Windows);
    }

    [Fact]
    public void A_popup_with_no_grab_is_a_tooltip()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var parent = Map(harness, 300, 200, "tooltip owner");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        _ = OpenPopup(harness, parent, 10, 10, 60, 20, grabSerial: null);
        harness.PumpUntil(() => toplevels.Popups.Any(e => e.Window is not null), "the tooltip window never opened");
        var tooltip = toplevels.Popups.Single().Window!.Window;
        var flags = SDL.GetWindowFlags(tooltip.Handle);
        Assert.True((flags & (tooltip.IsPopup ? SDL.WindowFlags.Tooltip : SDL.WindowFlags.Borderless)) != 0, $"flags {flags}");
    }

    [Fact]
    public void Host_window_states_reach_the_toplevel()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var states = new List<XdgToplevel.State>();
        var mapped = Map(harness, 200, 150, "states");
        mapped.Toplevel.Configure += (_, e) =>
        {
            states.Clear();
            var bytes = e.States;
            for (var i = 0; i + 4 <= bytes.Length; i += 4)
            {
                states.Add((XdgToplevel.State)BitConverter.ToUInt32(bytes, i));
            }
        };
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        var window = toplevels.Entries.Single().Window!;

        SdlEvents.Window(window.Window.Id, SDL.EventType.WindowMaximized);
        harness.PumpUntil(() => states.Contains(XdgToplevel.State.Maximized), "the client never saw the maximized state");

        SdlEvents.Window(window.Window.Id, SDL.EventType.WindowRestored);
        harness.PumpUntil(() => !states.Contains(XdgToplevel.State.Maximized), "the client never left the maximized state");

        SdlEvents.Window(window.Window.Id, SDL.EventType.WindowEnterFullscreen);
        harness.PumpUntil(() => states.Contains(XdgToplevel.State.Fullscreen), "the client never saw the fullscreen state");
    }

    [Theory]
    [MemberData(nameof(SdlLeaseTests.Leases), MemberType = typeof(SdlLeaseTests))]
    public void A_window_opened_after_the_first_closed_still_presents(string lease)
    {
        using var harness = new SdlCompHarness(lease, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var first = Map(harness, 200, 150, "first");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is { Presented: > 0 }), "the first window never presented");
        first.Toplevel.Dispose();
        first.Xdg.Dispose();
        first.Surface.Dispose();
        harness.PumpUntil(() => toplevels.Entries.Count == 0, "the first window outlived its toplevel");

        _ = Map(harness, 200, 150, "second");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is { Presented: > 0 }), "the second window never presented");
    }

    [Fact]
    public void Hidden_toplevel_windows_never_suspend_the_host()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, toplevels: true);
        var toplevels = harness.Comp.Toplevels!;
        var first = Map(harness, 200, 150, "hidden");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is not null), "no SDL window opened");
        SdlEvents.Window(toplevels.Entries.Single().Window!.Window.Id, SDL.EventType.WindowOccluded);
        harness.Pump();
        Assert.False(harness.Host.IsSuspended);

        first.Toplevel.Dispose();
        first.Xdg.Dispose();
        first.Surface.Dispose();
        harness.PumpUntil(() => toplevels.Entries.Count == 0, "the hidden window outlived its toplevel");
        Assert.False(harness.Host.IsSuspended);

        _ = Map(harness, 120, 90, "after");
        harness.PumpUntil(() => toplevels.Entries.Any(e => e.Window is { Presented: > 0 }), "the next window never presented");
    }
}
