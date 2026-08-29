using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Protocol;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DispatchAllocationTests
{
    private const int Rounds = 100;

    [Fact]
    public void A_commit_carrying_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 3; i++)
        {
            Commits(host, surface, buffer, Rounds);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Commits(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "commit-with-frame-callback", allocated);
    }

    [Fact]
    public void Firing_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 10; i++)
        {
            Commits(host, surface, buffer, 1);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Commits(host, surface, buffer, Rounds);
        host.Loop.Dispatch(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Output.StepFrame();
        host.RenderFrame();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "frame-callback-fire", allocated);
    }

    [Fact]
    public void An_attach_and_damage_without_a_frame_callback_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        for (var i = 0; i < 3; i++)
        {
            Attaches(host, surface, buffer, Rounds);
            host.Loop.Dispatch(0);
            host.Output.StepFrame();
            host.RenderFrame();
        }

        Attaches(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "attach-and-damage", allocated);
    }

    [Fact]
    public void A_commit_swapping_between_two_buffers_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var front = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        var back = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));

        var allocated = 0L;
        for (var round = 0; round < 2 * Rounds; round++)
        {
            using (var callback = surface.Frame())
            {
                surface.Attach((round % 2 == 0 ? front : back).Proxy, 0, 0);
                surface.Damage(0, 0, 64, 48);
                surface.Commit();
                host.Client.Display.Flush();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            host.Loop.Dispatch(0);
            var measured = GC.GetAllocatedBytesForCurrentThread() - before;
            host.Output.StepFrame();
            host.RenderFrame();
            if (round >= Rounds)
            {
                allocated += measured;
            }
        }

        Budgets.Check("server", "commit-swapping-buffers", allocated);
    }

    [Fact]
    public void A_popup_map_and_unmap_round_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var client = host.Client;
        var parent = MappedToplevel.Map(host, client);
        var placer = new PopupPlacer(host.Layout);
        var parentTree = new SceneTree(host.Scene.Root);
        host.Shell.NewPopup += popup => placer.Attach(popup, parentTree);
        var buffer = client.CreateBuffer(30, 30, Fill.Solid(30, 30, 0xFF884422));

        for (var i = 0; i < 20; i++)
        {
            PopupRound(host, client, parent, buffer, out _);
        }

        var allocated = 0L;
        for (var round = 0; round < Rounds; round++)
        {
            PopupRound(host, client, parent, buffer, out var measured);
            allocated += measured;
        }

        Budgets.Check("server", "popup-cycle", allocated);
    }

    [Fact]
    public void Setting_a_colour_representation_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var manager = new ColorRepresentationManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind<Basin.Desktop.Protocol.WpColorRepresentationManagerV1>(
            host, "wp_color_representation_manager_v1", ColorRepresentationManager.Version);
        var representation = proxy.GetSurface(window.Surface);
        host.PumpToClient();

        RepresentationRounds(host, representation, Rounds);
        host.Loop.Dispatch(0);

        RepresentationRounds(host, representation, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "color-representation-set", allocated);
    }

    [Fact]
    public void A_frame_with_a_client_committing_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        var window = MappedToplevel.Map(host, host.Client);
        var front = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));
        var back = host.Client.CreateBuffer(60, 50, Fill.Gradient(60, 50));

        for (var round = 0; round < Rounds; round++)
        {
            ClientFrame(host, window, round % 2 == 0 ? front : back, round);
            ServerFrame(host, sceneOutput, swapchain, state, options, round);
        }

        var allocated = 0L;
        for (var round = 0; round < Rounds; round++)
        {
            ClientFrame(host, window, round % 2 == 0 ? front : back, round);
            var before = GC.GetAllocatedBytesForCurrentThread();
            ServerFrame(host, sceneOutput, swapchain, state, options, round);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Budgets.Check("server", "frame-with-a-client", allocated);
    }

    [Fact]
    public void A_layer_surface_repainting_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        using var layerShell = new Basin.Shell.Xdg.LayerShell(host.Display, host.Compositor);
        var layers = new Basin.Scene.SceneLayers(host.Scene.Root);
        var driver = new LayerShellSceneDriver(layerShell, host.Layout, layers);
        var client = host.Client;

        var shellProxy = Bind<Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1>(host, "zwlr_layer_shell_v1", 4);
        var surface = client.Compositor.CreateSurface();
        var layerProxy = shellProxy.GetLayerSurface(
            surface, client.Outputs[0], Basin.Shell.Xdg.Protocol.ZwlrLayerShellV1.Layer.Top, "panel");
        layerProxy.SetSize(200, 30);
        var acked = 0u;
        layerProxy.Configure += (_, e) =>
        {
            acked = e.Serial;
            layerProxy.AckConfigure(e.Serial);
        };
        surface.Commit();
        host.PumpUntil(() => acked != 0);

        SceneSurface? panel = null;
        driver.SceneCreated += (_, scene) => panel = scene;
        var buffer = client.CreateBuffer(200, 30, Fill.Solid(200, 30, 0xFF285577));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 30);
        surface.Commit();
        host.PumpUntil(() => panel is not null);

        LayerRepaints(host, surface, buffer, Rounds);
        host.Loop.Dispatch(0);

        LayerRepaints(host, surface, buffer, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", "layer-surface-repaint", allocated);
    }

    [Fact]
    public void Pointer_axis_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.PumpToClient();

        for (var i = 0; i < Rounds; i++)
        {
            AxisRound(host, i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            AxisRound(host, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "pointer-axis", allocated);
    }

    [Fact]
    public void The_desktop_pack_fan_out_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var foreignToplevels = new ForeignToplevelManager(host.Display, model);
        using var toplevelList = new ForeignToplevelListManager(host.Display, model);
        using var captureSources = new ImageCaptureSourceManager(host.Display);

        var wlr = Bind<Basin.Desktop.Protocol.ZwlrForeignToplevelManagerV1>(
            host, "zwlr_foreign_toplevel_manager_v1", ForeignToplevelManager.Version);
        var ext = Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(
            host, "ext_foreign_toplevel_list_v1", ForeignToplevelListManager.Version);
        Assert.NotNull(wlr);
        Assert.NotNull(ext);

        var id = model.Add("a title", "an.app.id");
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "desktop-pack-fan-out", allocated);
    }

    [Fact]
    public void Pointer_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Pointer.NotifyEnter(window.ServerSurface, 10, 10);
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            PointerRound(host, i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            PointerRound(host, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "pointer-delivery", allocated);
    }

    [Fact]
    public void Key_delivery_stays_within_budget()
    {
        Budgets.Require();

        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.SetKeymap();
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        for (var i = 0; i < 20; i++)
        {
            KeyRound(host, (uint)i);
        }

        host.PumpToClient();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            KeyRound(host, (uint)i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "key-delivery", allocated);
    }

    [Fact]
    public void A_river_repaint_round_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        var window = fixture.MapToplevel();
        var buffer = fixture.Host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, 0xff336699));

        for (var i = 0; i < 10; i++)
        {
            RepaintRound(fixture, window, buffer);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Rounds; i++)
        {
            RepaintRound(fixture, window, buffer);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Budgets.Check("server", "river-repaint-round", allocated);
    }

    private static void Commits(CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var callback = surface.Frame();
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    [Fact]
    public void A_dmabuf_import_stays_within_budget()
    {
        Budgets.Require();
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        using var host = new CompositorTestHost();

        var warm = Imports(host, Rounds);
        host.Loop.Dispatch(0);
        Release(host, warm);

        var measured = Imports(host, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Release(host, measured);
        Budgets.Check("server", "dmabuf-import", allocated);
    }

    [Fact]
    public void A_window_manager_layout_round_stays_within_budget()
    {
        Budgets.Require();

        using var fixture = new RiverFixture();
        _ = fixture.MapToplevel();

        var round = 0;
        var measuring = false;
        var allocated = 0L;

        fixture.OnManage = context =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var window in context.Windows)
            {
                window.ProposeDimensions(80 + (round % 20), 60 + (round % 10));
            }

            if (measuring)
            {
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            round++;
        };

        for (var i = 0; i < Rounds; i++)
        {
            fixture.RequestManageAndSettle();
        }

        measuring = true;
        for (var i = 0; i < Rounds; i++)
        {
            fixture.RequestManageAndSettle();
        }

        Budgets.Check("client", "wm-layout-round", allocated);
    }

    [Fact]
    public void A_remote_frame_arriving_over_a_channel_stays_within_budget()
    {
        Budgets.Require();

        const int width = 64;
        const int height = 48;

        using var transport = new Basin.Transport.Waypipe.WaypipeClientTransport();
        using var engine = new Basin.Transport.Waypipe.WaypipeEngine(
            transport, Basin.Transport.Waypipe.WaypipeCompression.None);

        var open = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(4), width * height * 4);
        engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.OpenFile, open);

        var diff = new byte[12 + 8 + 4096];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 8 + 4096);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 4096 / 4);

        var protocol = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol.AsSpan(4), (8u << 16) | 6u);

        var drain = new byte[4096];
        var slots = new int[8];

        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);

        var before = GC.GetAllocatedBytesForCurrentThread();
        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("channel", "remote-frame", allocated);
    }

    [Fact]
    public void A_remote_dmabuf_frame_arriving_over_a_channel_stays_within_budget()
    {
        Budgets.Require();

        const int width = 64;
        const int height = 48;

        using var transport = new Basin.Transport.Waypipe.WaypipeClientTransport();
        using var engine = new Basin.Transport.Waypipe.WaypipeEngine(
            transport,
            Basin.Transport.Waypipe.WaypipeCompression.None,
            options: new Basin.Transport.Waypipe.WaypipeChannelOptions { CarriesDmabuf = true });

        var open = new byte[8 + 64];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(4), width * height * 4);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(8), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(12), height);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(16), (uint)DrmFormat.Xrgb8888);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(open.AsSpan(20), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(40), width * 4);
        open[64] = 1;
        engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.OpenDmabuf, open);

        var diff = new byte[12 + 8 + 4096];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(4), 8 + 4096);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(diff.AsSpan(8), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(12), 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(diff.AsSpan(16), 4096 / 4);

        var protocol = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol, 4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(protocol.AsSpan(4), (8u << 16) | 6u);

        var drain = new byte[4096];
        var slots = new int[8];

        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);

        var before = GC.GetAllocatedBytesForCurrentThread();
        RemoteFrames(engine, transport, diff, protocol, drain, slots, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("channel", "remote-dmabuf-frame", allocated);
    }

    private static void RemoteFrames(
        Basin.Transport.Waypipe.WaypipeEngine engine,
        Basin.Transport.Waypipe.WaypipeClientTransport transport,
        byte[] diff,
        byte[] protocol,
        byte[] drain,
        int[] slots,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.BufferDiff, diff);
            engine.Apply(Basin.Transport.Waypipe.WaypipeMessageType.Protocol, protocol);
            transport.TryReadNonBlocking(drain, Memory<byte>.Empty, slots, Memory<int>.Empty);
        }
    }

    private static void Attaches(CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    private sealed class Imported
    {
        public readonly List<WlBuffer> Buffers = [];

        public readonly List<ZwpLinuxBufferParamsV1> Params = [];

        public readonly List<int> Fds = [];
    }

    private static Imported Imports(CompositorTestHost host, int count)
    {
        const int Width = 64;
        const int Height = 48;
        const int Stride = Width * 4;

        var made = new Imported();
        for (var i = 0; i < count; i++)
        {
            var fd = PlaneFd(Stride * Height);
            var parameters = host.Client.Dmabuf!.CreateParams();
            parameters.Add(fd, 0, 0, Stride, 0, 0);
            parameters.Created += (_, e) => made.Buffers.Add(e.Buffer);
            parameters.Create(Width, Height, (uint)DrmFormat.Argb8888, 0);
            made.Params.Add(parameters);
            made.Fds.Add(fd);
        }

        host.Client.Display.Flush();
        return made;
    }

    private static void Release(CompositorTestHost host, Imported made)
    {
        host.PumpToClient();

        foreach (var buffer in made.Buffers)
        {
            buffer.Dispose();
        }

        foreach (var parameters in made.Params)
        {
            parameters.Dispose();
        }

        foreach (var fd in made.Fds)
        {
            CloseFd(fd);
        }

        host.PumpToClient();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    private static int PlaneFd(int size)
    {
        var fd = memfd_create("basin-budget-plane", 1);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, size));
        return fd;
    }

    private static void PopupRound(
        CompositorTestHost host,
        ShmTestClient client,
        MappedToplevel parent,
        ClientShmBuffer buffer,
        out long allocated)
    {
        var positioner = client.WmBase!.CreatePositioner();
        positioner.SetSize(30, 30);
        positioner.SetAnchorRect(5, 5, 1, 1);
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase.GetXdgSurface(surface);
        var popup = xdgSurface.GetPopup(parent.XdgSurface, positioner);
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);
        surface.Commit();
        client.Display.Flush();

        var before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        host.PumpToClient();

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 30, 30);
        surface.Commit();
        popup.Destroy();
        xdgSurface.Destroy();
        surface.Dispose();
        positioner.Destroy();
        client.Display.Flush();

        before = GC.GetAllocatedBytesForCurrentThread();
        host.Loop.Dispatch(0);
        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void RepresentationRounds(
        CompositorTestHost host,
        Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1 representation,
        int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            representation.SetAlphaMode(i % 2 == 0
                ? Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.AlphaMode.Straight
                : Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.AlphaMode.PremultipliedElectrical);
            representation.SetCoefficientsAndRange(
                Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Coefficients.Bt709,
                i % 2 == 0
                    ? Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Range.Limited
                    : Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.Range.Full);
            representation.SetChromaLocation(
                Basin.Desktop.Protocol.WpColorRepresentationSurfaceV1.ChromaLocation.Type0);
        }

        host.Client.Display.Flush();
    }

    private static void ClientFrame(
        CompositorTestHost host, MappedToplevel window, ClientShmBuffer buffer, int round)
    {
        using var callback = window.Surface.Frame();
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Damage(0, round % 4, 60, 20);
        window.Surface.Commit();
        host.Client.Display.Flush();
    }

    private static void ServerFrame(
        CompositorTestHost host,
        SceneOutput sceneOutput,
        Swapchain swapchain,
        OutputState state,
        in SceneCommitOptions options,
        int round)
    {
        host.Loop.Dispatch(0);
        _ = sceneOutput.Commit(host.Renderer, swapchain, state, options);
        host.Output.StepFrame();
        host.Scene.SendFrameDone((uint)(round * 16));
    }

    private static void LayerRepaints(
        CompositorTestHost host, WlSurface surface, ClientShmBuffer buffer, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, i % 4, 200, 10);
            surface.Commit();
        }

        host.Client.Display.Flush();
    }

    private static void AxisRound(CompositorTestHost host, int i)
    {
        host.Seat.Pointer.NotifyAxis(
            (uint)i,
            new PointerAxis(WlPointer.Axis.VerticalScroll, 10 + (i % 5), 120));
        host.Seat.Pointer.NotifyFrame();
    }

    private static void PointerRound(CompositorTestHost host, int i)
    {
        host.Seat.Pointer.NotifyMotion((uint)i, 10 + (i % 20), 10 + (i % 20));
        host.Seat.Pointer.NotifyFrame();
    }

    private static void KeyRound(CompositorTestHost host, uint i)
    {
        host.Seat.Keyboard.NotifyKey(i, 30, true);
        host.Seat.Keyboard.NotifyKey(i + 1, 30, false);
    }

    private static void RepaintRound(RiverFixture fixture, MappedToplevel window, ClientShmBuffer buffer)
    {
        window.Surface.Attach(buffer.Proxy, 0, 0);
        window.Surface.Damage(0, 0, 40, 40);
        window.Surface.Commit();
        fixture.Settle(2);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, int version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, (uint)version);
            }
        };

        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}
