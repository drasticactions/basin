using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SurfacePresenceTests
{
    [Fact]
    public void Enter_and_leave_follow_the_surface_across_outputs()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var secondState = new OutputState();
        second.Commit(secondState.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)));
        var secondGlobal = new OutputGlobal(host.Display, second);
        host.Layout.Add(second, 160, 0);
        host.TrackOutputGlobal(second, secondGlobal);

        var client = host.ConnectClient();
        Assert.Equal(2, client.Outputs.Count);

        var entered = new List<string>();
        var left = new List<string>();
        var surface = client.Compositor.CreateSurface();
        surface.Enter += (_, _) => entered.Add("enter");
        surface.Leave += (_, _) => left.Add("leave");
        var buffer = client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverSurface = host.SurfaceScenes[0].Surface;
        var node = host.SurfaceScenes[0];

        node.Tree.SetPosition(10, 10);
        UpdatePresence(host, serverSurface, node);
        host.PumpToClient();
        Assert.Single(entered);
        Assert.Empty(left);

        node.Tree.SetPosition(130, 10);
        UpdatePresence(host, serverSurface, node);
        host.PumpToClient();
        Assert.Equal(2, entered.Count);
        Assert.Empty(left);

        node.Tree.SetPosition(200, 10);
        UpdatePresence(host, serverSurface, node);
        host.PumpToClient();
        Assert.Equal(2, entered.Count);
        Assert.Single(left);

        surface.Dispose();
        host.PumpToServer();
        secondGlobal.Dispose();
        second.Destroy();
    }

    private static void UpdatePresence(CompositorTestHost host, Surface surface, SceneSurface node)
    {
        var box = new Box(node.Tree.X, node.Tree.Y, surface.Current.Width, surface.Current.Height);
        foreach (var (output, global) in host.OutputGlobals())
        {
            var outputBox = host.Layout.BoxOf(output);
            var overlaps = box.X < outputBox.Right && box.Right > outputBox.X &&
                           box.Y < outputBox.Bottom && box.Bottom > outputBox.Y;
            surface.SetOutputPresence(global, overlaps);
        }
    }
}

public sealed class OutputPositionTests
{
    [Fact]
    public void The_layout_position_reaches_wl_output_geometry()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var secondState = new OutputState();
        second.Commit(secondState.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)));
        var secondGlobal = new OutputGlobal(host.Display, second);
        host.Layout.Add(second, 160, 0);
        host.TrackOutputGlobal(second, secondGlobal);

        var client = host.ConnectClient();
        Assert.Equal(2, client.Outputs.Count);

        var positions = new List<(int X, int Y)>();
        foreach (var proxy in client.Outputs)
        {
            proxy.Geometry += (_, e) => positions.Add((e.X, e.Y));
        }

        host.Layout.Move(second, 200, 40);
        host.PumpToClient();
        Assert.Contains((200, 40), positions);

        positions.Clear();
        host.Layout.Move(second, 200, 40);
        host.PumpToClient();
        Assert.Empty(positions);

        secondGlobal.Dispose();
        second.Destroy();
    }

    [Fact]
    public void An_xdg_output_repeats_neither_its_name_nor_an_unchanged_box()
    {
        using var host = new CompositorTestHost();
        using var manager = new Basin.Desktop.XdgOutputManager(host.Display, host.Layout);

        var client = host.ConnectClient();
        Basin.Desktop.Protocol.ZxdgOutputManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zxdg_output_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZxdgOutputManagerV1>(e.Name, 3);
            }
        };
        host.PumpToClient();

        var boxes = new List<(int X, int Y, int Width, int Height)>();
        var names = new List<string>();
        var descriptions = new List<string>();
        var dones = 0;
        var xdgOutput = proxy!.GetXdgOutput(client.Outputs[0]);
        var position = (X: 0, Y: 0);
        xdgOutput.LogicalPosition += (_, e) => position = (e.X, e.Y);
        xdgOutput.LogicalSize += (_, e) => boxes.Add((position.X, position.Y, e.Width, e.Height));
        xdgOutput.Name += (_, e) => names.Add(e.Name);
        xdgOutput.Description += (_, e) => descriptions.Add(e.Description);
        client.Outputs[0].Done += (_, _) => dones++;
        host.PumpToClient();

        Assert.Single(boxes);
        Assert.Single(names);
        Assert.Single(descriptions);
        Assert.Equal(1, dones);

        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var secondState = new OutputState();
        second.Commit(secondState.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)));
        var secondGlobal = new OutputGlobal(host.Display, second);
        host.Layout.Add(second, 400, 0);
        host.TrackOutputGlobal(second, secondGlobal);
        host.PumpToClient();

        Assert.Single(boxes);
        Assert.Single(names);
        Assert.Equal(1, dones);

        host.Layout.Move(host.Output, 32, 16);
        host.PumpToClient();

        Assert.Equal(2, boxes.Count);
        Assert.Equal((32, 16), (boxes[1].X, boxes[1].Y));
        Assert.Single(names);
        Assert.Single(descriptions);

        xdgOutput.Dispose();
        proxy.Dispose();
        host.PumpToServer();
        secondGlobal.Dispose();
        second.Destroy();
    }
}

public sealed class SoftwareCursorTests
{
    [Fact]
    public void Software_cursor_composites_and_damages_on_move()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0, 0, 0, 1));

        var cursor = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        Assert.True(cursor.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < 8; y++)
            {
                var row = (uint*)(view.Data + y * view.Stride);
                for (var x = 0; x < 8; x++)
                {
                    row[x] = 0xFFFFFFFF;
                }
            }
        }

        cursor.EndDataAccess();
        sceneOutput.SetSoftwareCursor(cursor, 0, 0);
        sceneOutput.MoveSoftwareCursor(20, 30);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        AssertPixel(state.Buffer!, 21, 31, 0xFFFFFFFF);
        AssertPixel(state.Buffer!, 40, 40, 0xFF000000);

        sceneOutput.MoveSoftwareCursor(50, 60);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        AssertPixel(state.Buffer!, 21, 31, 0xFF000000);
        AssertPixel(state.Buffer!, 51, 61, 0xFFFFFFFF);

        sceneOutput.SetSoftwareCursor(null, 0, 0);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        AssertPixel(state.Buffer!, 51, 61, 0xFF000000);

        cursor.Destroy();
    }

    private static void AssertPixel(IBuffer buffer, int x, int y, uint expected)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var actual = *(uint*)(view.Data + y * view.Stride + x * 4) | 0xFF000000u;
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }
}

public sealed class ScanoutFeedbackTests
{
    private const uint ScanoutFlag = 1;
    private const uint SamplingFlag = 2;

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Candidate_surfaces_receive_a_scanout_tranche()
    {
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        using var host = new CompositorTestHost();

        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();
        var serverSurface = host.Compositor.Surfaces.Single();

        var feedback = host.Client.Dmabuf!.GetSurfaceFeedback(surface);
        var trancheFlags = new List<uint>();
        var doneCount = 0;
        feedback.FormatTable += (_, e) => close(e.Fd);
        feedback.TrancheFlagsEvent += (_, e) => trancheFlags.Add((uint)e.Flags);
        feedback.Done += (_, _) => doneCount++;
        host.PumpUntil(() => doneCount >= 1);
        Assert.Equal([SamplingFlag], trancheFlags);

        var scanout = new DrmFormatSet();
        scanout.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        trancheFlags.Clear();
        host.Dmabuf!.SetScanoutTargets(serverSurface, scanout);
        host.PumpUntil(() => doneCount >= 2);
        Assert.Equal([ScanoutFlag | SamplingFlag, SamplingFlag], trancheFlags);

        trancheFlags.Clear();
        host.Dmabuf.SetScanoutTargets(serverSurface, null);
        host.PumpUntil(() => doneCount >= 3);
        Assert.Equal([SamplingFlag], trancheFlags);

        surface.Dispose();
        host.PumpToServer();
    }
}
