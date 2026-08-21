using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void Attach_commit_render_shows_client_pixels()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 24, Fill.Solid(32, 24, 0xFF3366CC));

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 24);
        surface.Commit();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xFF3366CCu, host.Pixel(0, 0));
        Assert.Equal(0xFF3366CCu, host.Pixel(31, 23));
        Assert.Equal(0xFF000000u, host.Pixel(32, 24));

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Old_buffer_released_when_replaced()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var first = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFFFF0000));
        var second = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF00FF00));
        first.TrackRelease();

        surface.Attach(first.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        host.PumpToClient();
        Assert.False(first.Released);

        surface.Attach(second.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        host.RenderFrame();
        host.PumpToClient();

        Assert.True(first.Released);
        Assert.Equal(0xFF00FF00u, host.Pixel(0, 0));

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Frame_callback_fires_once_after_render()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(8, 8, Fill.Solid(8, 8, 0xFFFFFFFF));

        var done = 0;
        var callback = surface.Frame();
        callback.Done += (_, _) => done++;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(0, done);

        host.RenderFrame();
        host.PumpToClient();
        Assert.Equal(1, done);

        host.RenderFrame();
        host.PumpToClient();
        Assert.Equal(1, done);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Damage_and_damage_buffer_accumulate_separately()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xFF102030));

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(1, 1, 4, 4);
        surface.DamageBuffer(8, 8, 2, 2);
        surface.Commit();
        host.PumpToServer();

        var serverSurface = host.SurfaceScenes[0].Surface;
        var surfaceExtents = serverSurface.Current.SurfaceDamage.Extents;
        var bufferExtents = serverSurface.Current.BufferDamage.Extents;
        Assert.Equal((1, 1, 5, 5), (surfaceExtents.X1, surfaceExtents.Y1, surfaceExtents.X2, surfaceExtents.Y2));
        Assert.Equal((8, 8, 10, 10), (bufferExtents.X1, bufferExtents.Y1, bufferExtents.X2, bufferExtents.Y2));

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Invalid_buffer_scale_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        surface.SetBufferScale(0);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Second_role_assignment_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var parentA = host.Client.Compositor.CreateSurface();
        var parentB = host.Client.Compositor.CreateSurface();

        host.Client.Subcompositor.GetSubsurface(surface, parentA);
        host.PumpToServer();
        var withRole = host.Compositor.Surfaces.Single(s => s.Role == Subsurface.RoleName);
        Assert.NotNull(withRole.SubsurfaceRole);

        host.Client.Subcompositor.GetSubsurface(surface, parentB);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Seat_advertises_its_capabilities()
    {
        using var host = new CompositorTestHost();
        var globals = new List<(string Interface, uint Name, uint Version)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => globals.Add((e.Interface, e.Name, e.Version));
        host.PumpToClient();

        var seatGlobal = globals.Single(g => g.Interface == "wl_seat");
        var seat = registry.Bind<WlSeat>(seatGlobal.Name, 5);
        WlSeat.Capability? caps = null;
        seat.Capabilities += (_, e) => caps = e.Capabilities;
        host.PumpToClient();

        Assert.Equal(WlSeat.Capability.Pointer | WlSeat.Capability.Keyboard | WlSeat.Capability.Touch, caps);
        seat.Dispose();
        registry.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_core_globals_advertise_the_released_core_versions()
    {
        using var host = new CompositorTestHost();
        var globals = new List<(string Interface, uint Name, uint Version)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => globals.Add((e.Interface, e.Name, e.Version));
        host.PumpToClient();

        Assert.Equal(11u, globals.Single(g => g.Interface == "wl_seat").Version);
        Assert.Equal(4u, globals.Single(g => g.Interface == "wl_data_device_manager").Version);

        var seat = registry.Bind<WlSeat>(globals.Single(g => g.Interface == "wl_seat").Name, 9);
        WlSeat.Capability? caps = null;
        seat.Capabilities += (_, e) => caps = e.Capabilities;
        host.PumpToClient();
        Assert.NotNull(caps);

        seat.Dispose();
        registry.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_data_device_manager_services_its_version_four_destructor()
    {
        using var host = new CompositorTestHost();
        var globals = new List<(string Interface, uint Name, uint Version)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => globals.Add((e.Interface, e.Name, e.Version));
        host.PumpToClient();

        var manager = registry.Bind<WlDataDeviceManager>(
            globals.Single(g => g.Interface == "wl_data_device_manager").Name, 4);
        host.PumpToServer();

        manager.Release();
        host.PumpToServer();
        host.PumpToClient();

        registry.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_release_callback_fires_when_the_next_commit_displaces_its_buffer()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = host.Client.Compositor.CreateSurface();
        var first = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xff0000ff));
        var second = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xff00ff00));

        var released = 0;
        surface.Attach(first.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        var release = surface.GetRelease();
        release.Done += (_, e) =>
        {
            released++;
            Assert.Equal(0u, e.CallbackData);
        };
        surface.Commit();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal(0, released);

        surface.Attach(second.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        surface.Commit();
        host.PumpToServer();
        host.PumpUntil(() => released == 1);

        surface.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void Get_release_without_a_buffer_in_the_same_update_is_no_buffer()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = host.Client.Compositor.CreateSurface();

        _ = surface.GetRelease();
        surface.Commit();

        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void Output_advertises_mode_scale_and_name()
    {
        using var host = new CompositorTestHost(320, 200);
        var globals = new List<(string Interface, uint Name, uint Version)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => globals.Add((e.Interface, e.Name, e.Version));
        host.PumpToClient();

        var outputGlobal = globals.Single(g => g.Interface == "wl_output");
        Assert.Equal(4u, outputGlobal.Version);
        var output = registry.Bind<WlOutput>(outputGlobal.Name, 4);
        (int W, int H, int Refresh)? mode = null;
        var scale = 0;
        string? name = null;
        var done = false;
        output.ModeEvent += (_, e) => mode = (e.Width, e.Height, e.Refresh);
        output.Scale += (_, e) => scale = e.Factor;
        output.Name += (_, e) => name = e.Name;
        output.Done += (_, _) => done = true;
        host.PumpToClient();

        Assert.Equal((320, 200, 60_000), mode);
        Assert.Equal(1, scale);
        Assert.Equal("HEADLESS-1", name);
        Assert.True(done);

        output.Dispose();
        registry.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Output_name_is_sent_once_and_frame_commits_resend_nothing()
    {
        using var host = new CompositorTestHost(320, 200);
        var names = 0;
        var dones = 0;
        var scale = 0;
        var output = host.Client.Outputs[0];
        output.Name += (_, _) => names++;
        output.Done += (_, _) => dones++;
        output.Scale += (_, e) => scale = e.Factor;

        host.RenderFrame();
        host.RenderFrame();
        host.PumpToClient();
        Assert.Equal(0, dones);
        Assert.Equal(0, names);

        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(2)));
        host.PumpToClient();
        Assert.Equal(2, scale);
        Assert.Equal(1, dones);
        Assert.Equal(0, names);
    }
    [Fact]
    public void The_default_pack_carries_the_globals_that_park_commits()
    {
        var wireInterfaces = Basin.Desktop.DesktopPack.Desktop.Select(m => m.WireInterface).ToList();

        Assert.Contains("wp_fifo_manager_v1", wireInterfaces);
        Assert.Contains("wp_commit_timing_manager_v1", wireInterfaces);
    }

}
