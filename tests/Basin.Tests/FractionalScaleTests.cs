using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputScalingTests
{
    [Fact]
    public void Snap_lands_on_120ths()
    {
        Assert.Equal(1.5, OutputScaling.Snap(1.5));
        Assert.Equal(160.0 / 120.0, OutputScaling.Snap(4.0 / 3.0));
        Assert.Equal(1.0 / 120.0, OutputScaling.Snap(0));
    }

    [Fact]
    public void Ceiling_scale_is_exact_on_integers()
    {
        Assert.Equal(1, OutputScaling.CeilScale(1.0));
        Assert.Equal(2, OutputScaling.CeilScale(1.25));
        Assert.Equal(2, OutputScaling.CeilScale(2.0));
        Assert.Equal(3, OutputScaling.CeilScale(2.5));
    }

    [Fact]
    public void Adjacent_logical_boxes_stay_adjacent_physically()
    {
        var left = OutputScaling.ToPhysical(new Box(0, 0, 101, 10), 1.5);
        var right = OutputScaling.ToPhysical(new Box(101, 0, 50, 10), 1.5);
        Assert.Equal(left.Right, right.X);
        Assert.Equal(0, left.X);
        Assert.Equal((int)Math.Round(101 * 1.5), right.X);
    }

    [Fact]
    public void Expanded_conversion_covers_the_rendered_box()
    {
        var logical = new Box(7, 3, 41, 23);
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var drawn = OutputScaling.ToPhysical(logical, scale);
            var damaged = OutputScaling.ToPhysicalExpanded(logical, scale);
            Assert.True(damaged.X <= drawn.X && damaged.Y <= drawn.Y);
            Assert.True(damaged.Right >= drawn.Right && damaged.Bottom >= drawn.Bottom);
        }

        Assert.Equal(OutputScaling.ToPhysical(logical, 2.0), OutputScaling.ToPhysicalExpanded(logical, 2.0));
    }
}

public sealed class FractionalScaleTests
{
    [Fact]
    public void Output_scale_snaps_and_wl_output_advertises_the_ceiling()
    {
        using var host = new CompositorTestHost();
        var factor = 0;
        host.Client.Outputs[0].Scale += (_, e) => factor = e.Factor;

        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(1.5)));
        host.PumpToClient();

        Assert.Equal(1.5, host.Output.Scale);
        Assert.Equal(2, factor);

        Assert.Equal(new Box(0, 0, 107, 80), host.Layout.BoxOf(host.Output));
        Assert.Equal((107, 80), host.Output.LogicalSize());
    }

    [Fact]
    public void Preferred_scale_reaches_the_client_once_per_change()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var scaleObject = host.Client.FractionalScale!.GetFractionalScale(surface);
        var received = new List<uint>();
        scaleObject.PreferredScale += (_, e) => received.Add(e.Scale);
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal([120u], received);

        var serverSurface = host.SurfaceScenes[0].Surface;
        host.FractionalScale.SetPreferredScale(serverSurface, 1.5);
        host.FractionalScale.SetPreferredScale(serverSurface, 1.5);
        host.PumpToClient();
        Assert.Equal([120u, 180u], received);

        scaleObject.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_surface_that_entered_no_output_hears_the_layout_scale_not_the_protocol_default()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(2.0)));
        host.PumpToClient();

        var surface = host.Client.Compositor.CreateSurface();
        var scaleObject = host.Client.FractionalScale!.GetFractionalScale(surface);
        var received = new List<uint>();
        scaleObject.PreferredScale += (_, e) => received.Add(e.Scale);
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal([240u], received);

        host.FractionalScale.SetPreferredScale(host.SurfaceScenes[0].Surface, 2.0);
        host.PumpToClient();
        Assert.Equal([240u], received);

        scaleObject.Dispose();
        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_scene_walk_reaches_a_client_that_renders_into_a_subsurface()
    {
        using var host = new CompositorTestHost();
        var parent = host.Client.Compositor.CreateSurface();
        var parentBuffer = host.Client.CreateBuffer(60, 40, Fill.Gradient(60, 40));
        parent.Attach(parentBuffer.Proxy, 0, 0);
        parent.Commit();

        var content = host.Client.Compositor.CreateSurface();
        var subsurface = host.Client.Subcompositor.GetSubsurface(content, parent);
        subsurface.SetPosition(4, 3);
        subsurface.SetDesync();
        var contentBuffer = host.Client.CreateBuffer(30, 20, Fill.Gradient(30, 20));
        content.Attach(contentBuffer.Proxy, 0, 0);
        content.Commit();
        parent.Commit();

        var scaleObject = host.Client.FractionalScale!.GetFractionalScale(content);
        var received = new List<uint>();
        scaleObject.PreferredScale += (_, e) => received.Add(e.Scale);
        host.PumpToServer();
        host.PumpToClient();
        Assert.Equal([120u], received);

        host.SurfaceScenes[0].Tree.SetPosition(10, 5);
        var collected = new List<SurfaceBox>();
        host.Scene.CollectSurfaces(collected);

        Assert.Equal(2, collected.Count);
        Assert.Equal(new Box(10, 5, 60, 40), collected[0].Box);
        Assert.Equal(new Box(14, 8, 30, 20), collected[1].Box);

        foreach (var entry in collected)
        {
            host.FractionalScale.SetPreferredScale(entry.Surface, 1.5);
        }

        host.PumpToClient();
        Assert.Equal([120u, 180u], received);

        scaleObject.Dispose();
        subsurface.Dispose();
        content.Dispose();
        parent.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Scene_output_matches_the_oracle_at_fractional_scale()
    {
        using var host = new CompositorTestHost();
        using var scaleState = new OutputState();
        Assert.True(host.Output.Commit(scaleState.SetScale(1.5)));

        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        host.SurfaceScenes[0].Tree.SetPosition(8, 6);
        var opaque = new SceneRect(host.Scene.Root, 30, 20, new RenderColor(0.2f, 0.6f, 0.3f, 1f));
        opaque.SetPosition(50, 40);
        var translucent = new SceneRect(host.Scene.Root, 40, 25, new RenderColor(0.8f, 0.2f, 0.2f, 0.5f));
        translucent.SetPosition(20, 30);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        host.RenderFrame();
        AssertSamePixels(host.Target, state.Buffer!);

        opaque.SetPosition(71, 53);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        host.RenderFrame();
        AssertSamePixels(host.Target, state.Buffer!);

        using var rescale = new OutputState();
        Assert.True(host.Output.Commit(rescale.SetScale(1.25)));
        Assert.True(sceneOutput.NeedsRepaint);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        host.RenderFrame();
        AssertSamePixels(host.Target, state.Buffer!);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Fractional_viewport_source_reaches_the_scene_unrounded()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Gradient(64, 64));

        viewport.SetSource(
            Wayland.WlFixed.FromDouble(8.5),
            Wayland.WlFixed.FromDouble(4.25),
            Wayland.WlFixed.FromDouble(39.5),
            Wayland.WlFixed.FromDouble(30.75));
        viewport.SetDestination(48, 35);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        Assert.Equal(8.5, host.SurfaceScenes[0].Surface.Current.ViewportSourceX);
        var content = FindBuffer(host.SurfaceScenes[0].Tree);
        Assert.NotNull(content);
        Assert.Equal(new FBox(8.5, 4.25, 39.5, 30.75), content.SourceBox);

        surface.Dispose();
        host.PumpToServer();
    }

    private static SceneBuffer? FindBuffer(SceneTree tree)
    {
        foreach (var child in tree.Children)
        {
            var found = child switch
            {
                SceneBuffer buffer => buffer,
                SceneTree subtree => FindBuffer(subtree),
                _ => null,
            };
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    [Fact]
    public void Viewport_source_outside_the_buffer_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(64, 64, Fill.Gradient(64, 64));

        viewport.SetSource(Wayland.WlFixed.FromInt(16), Wayland.WlFixed.FromInt(16), Wayland.WlFixed.FromInt(48), Wayland.WlFixed.FromInt(48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(48, host.SurfaceScenes[0].Surface.Current.ViewportSourceWidth);

        viewport.SetSource(Wayland.WlFixed.FromInt(32), Wayland.WlFixed.FromInt(32), Wayland.WlFixed.FromInt(48), Wayland.WlFixed.FromInt(48));
        surface.Commit();
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Rects_partially_outside_the_target_are_clipped_not_written()
    {
        using var host = new CompositorTestHost();
        var rect = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        rect.SetPosition(150, 110);
        var offLeft = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(0f, 1f, 0f, 1f));
        offLeft.SetPosition(-10, -10);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(host.Scene.Render(host.Renderer, target, RenderColor.Black, 1.5));
        }
        finally
        {
            target.Destroy();
        }
    }

    private static unsafe void AssertSamePixels(IBuffer expected, IBuffer actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var a));
        try
        {
            Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var b));
            try
            {
                for (var y = 0; y < expected.Height; y++)
                {
                    var rowA = new ReadOnlySpan<byte>((void*)(a.Data + y * a.Stride), expected.Width * 4);
                    var rowB = new ReadOnlySpan<byte>((void*)(b.Data + y * b.Stride), expected.Width * 4);
                    if (!rowA.SequenceEqual(rowB))
                    {
                        Assert.Fail($"row {y} differs between oracle and scene output");
                    }
                }
            }
            finally
            {
                actual.EndDataAccess();
            }
        }
        finally
        {
            expected.EndDataAccess();
        }
    }
}
