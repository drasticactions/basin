using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class ZoomTests
{
    public static TheoryData<string> Renderers => GoldenTests.Renderers;

    private static readonly SceneCommitOptions Black = new() { Background = RenderColor.Black };

    private sealed class FocusSource : IAccessibleFocus
    {
        public event Action<Box, AccessibleFocusKind>? Moved;

        public void Raise(Box box, AccessibleFocusKind kind) => Moved?.Invoke(box, kind);
    }

    private static SceneRect Populate(CompositorTestHost host)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Gradient(64, 48));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.PumpToServer();
        host.SurfaceScenes[0].Tree.SetPosition(50, 40);
        _ = new SceneRect(host.Scene.Root, 30, 20, new RenderColor(1f, 0.2f, 0.2f, 1f)) { };
        var moving = new SceneRect(host.Scene.Root, 12, 12, new RenderColor(0.2f, 0.9f, 0.3f, 1f));
        moving.SetPosition(70, 70);
        return moving;
    }

    private static MemoryBuffer Oracle(CompositorTestHost host, SceneOutput output)
    {
        var oracle = new MemoryBuffer(host.Target.Width, host.Target.Height, DrmFormat.Xrgb8888);
        Assert.True(host.Scene.Render(host.Renderer, oracle, new SceneRenderOptions
        {
            Background = RenderColor.Black,
            Projection = output.Projection,
            OriginX = output.Position.X,
            OriginY = output.Position.Y,
        }));
        return oracle;
    }

    private static void AssertSame(MemoryBuffer expected, IBuffer actual)
    {
        var left = Diagnostics.BufferCapture.ReadRgba(expected);
        var right = Diagnostics.BufferCapture.ReadRgba((MemoryBuffer)actual);
        for (var i = 0; i < left.Length; i += 4)
        {
            if (left[i] != right[i] || left[i + 1] != right[i + 1] || left[i + 2] != right[i + 2])
            {
                var pixel = i / 4;
                Assert.Fail($"pixel ({pixel % expected.Width}, {pixel / expected.Width}) differs from the oracle");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Golden_zoom_at_two(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        Populate(host);
        var output = host.SceneOutput;
        output.SetZoom(2, 75, 60);
        using var state = new OutputState();
        Assert.True(output.Commit(host.Renderer, host.Target, 0, state, Black));
        Golden.AssertMatches(host, renderer == "pixman" ? "zoom-2x" : $"zoom-2x-{renderer}");
    }

    [Theory]
    [InlineData(OutputTransform.Normal)]
    [InlineData(OutputTransform.Rotate90)]
    [InlineData(OutputTransform.Flipped270)]
    public void A_zoomed_commit_matches_the_zoomed_oracle_after_damage_and_a_pan(OutputTransform transform)
    {
        using var host = new CompositorTestHost();
        using (var rotate = new OutputState())
        {
            Assert.True(host.Output.Commit(rotate.SetTransform(transform)));
        }

        var moving = Populate(host);
        var output = host.SceneOutput;
        output.SetZoom(2.5, 70, 55);
        Assert.Equal(2.5, output.ZoomFactor);
        var target = new MemoryBuffer(host.Target.Width, host.Target.Height, DrmFormat.Xrgb8888);
        using var state = new OutputState();
        try
        {
            Assert.True(output.Commit(host.Renderer, target, 0, state, Black));
            using (var oracle = new BufferGuard(Oracle(host, output)))
            {
                AssertSame(oracle.Buffer, target);
            }

            moving.SetPosition(74, 66);
            Assert.True(output.Commit(host.Renderer, target, 1, state, Black));
            using (var oracle = new BufferGuard(Oracle(host, output)))
            {
                AssertSame(oracle.Buffer, target);
            }

            output.SetZoom(2.5, 40, 35);
            Assert.True(output.Commit(host.Renderer, target, 1, state, Black));
            using (var oracle = new BufferGuard(Oracle(host, output)))
            {
                AssertSame(oracle.Buffer, target);
            }
        }
        finally
        {
            target.Destroy();
        }
    }

    [Fact]
    public void The_visible_area_stays_inside_the_output_and_one_turns_zoom_off()
    {
        using var host = new CompositorTestHost();
        var output = host.SceneOutput;
        output.SetZoom(4, 0, 0);
        Assert.Equal((0.0, 0.0, 40.0, 30.0), output.ZoomArea);
        output.SetZoom(40, 1000, 1000);
        Assert.Equal(SceneOutput.MaxZoom, output.ZoomFactor);
        Assert.Equal((150.0, 112.5, 10.0, 7.5), output.ZoomArea);
        output.SetZoom(1, 10, 10);
        Assert.False(output.IsZoomed);
        Assert.Equal(OutputProjection.For(host.Output), output.Projection);
        Assert.Equal((0.0, 0.0, 160.0, 120.0), output.ZoomArea);
    }

    [Fact]
    public void Planes_and_scanout_are_off_while_zoomed()
    {
        using var host = new CompositorTestHost();
        Populate(host);
        var options = new SceneCommitOptions { Background = RenderColor.Black, AllowPlaneOffload = true, AllowDirectScanout = true };
        host.CommitFrame(options);
        Assert.Null(host.SceneOutput.OffloadSkipped);
        host.SceneOutput.SetZoom(2, 80, 60);
        host.CommitFrame(options);
        Assert.Equal("zoomed output", host.SceneOutput.OffloadSkipped);
        Assert.Equal(0, host.SceneOutput.ScanoutCommits);
    }

    [Fact]
    public void The_software_cursor_is_drawn_at_the_zoomed_place_and_size()
    {
        using var host = new CompositorTestHost();
        var output = host.SceneOutput;
        var cursor = new MemoryBuffer(4, 4, DrmFormat.Argb8888);
        Assert.True(cursor.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < 4; y++)
            {
                var row = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < 4; x++)
                {
                    row[x] = 0xFFFFFFFF;
                }
            }
        }

        cursor.EndDataAccess();
        try
        {
            output.SetSoftwareCursor(cursor, 0, 0);
            output.MoveSoftwareCursor(60, 40);
            output.SetZoom(2, 80, 60);
            using var state = new OutputState();
            Assert.True(output.Commit(host.Renderer, host.Target, 0, state, Black));
            Assert.Equal(0xFFFFFFFFu, host.Pixel(40, 20));
            Assert.Equal(0xFFFFFFFFu, host.Pixel(47, 27));
            Assert.Equal(0xFF000000u, host.Pixel(48, 28));
            Assert.Equal(0xFF000000u, host.Pixel(39, 19));
            output.SetSoftwareCursor(null, 0, 0);
        }
        finally
        {
            cursor.Destroy();
        }
    }

    [Fact]
    public void A_capture_of_a_zoomed_output_is_not_zoomed()
    {
        using var host = new CompositorTestHost();
        Populate(host);
        host.SceneOutput.SetZoom(3, 60, 50);
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var source = CaptureSource.Output(host.Output);
        Assert.True(capture.TryDescribe(source, out var format));
        var shot = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        var plain = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(capture.Capture(source, default, shot));
            Assert.True(host.Scene.Render(host.Renderer, plain, new SceneRenderOptions
            {
                Background = RenderColor.Black,
                Projection = OutputProjection.For(host.Output),
            }));
            AssertSame(plain, shot);
        }
        finally
        {
            shot.Destroy();
            plain.Destroy();
        }
    }

    [Fact]
    public void The_controller_follows_the_pointer_the_focus_and_the_caret()
    {
        using var host = new CompositorTestHost();
        var output = host.SceneOutput;
        var pointer = new LayoutPointer(host.Layout);
        pointer.Warp(40, 30);
        using var zoom = new OutputZoom(host.Scene, pointer);
        var focus = new FocusSource();
        zoom.Track(focus);
        var changes = 0;
        zoom.Changed += () => changes++;

        zoom.Toggle();
        Assert.Equal(2.0, zoom.Factor);
        Assert.Equal(1, changes);
        Assert.Equal((0.0, 0.0, 80.0, 60.0), output.ZoomArea);

        pointer.Warp(70, 30);
        Assert.Equal((0.0, 0.0, 80.0, 60.0), output.ZoomArea);
        pointer.Warp(90, 70);
        Assert.Equal((10.0, 10.0, 80.0, 60.0), output.ZoomArea);

        focus.Raise(new Box(120, 90, 20, 10), AccessibleFocusKind.Focus);
        Assert.Equal((60.0, 40.0, 80.0, 60.0), output.ZoomArea);

        zoom.Follow = ZoomFollow.Pointer | ZoomFollow.Focus;
        focus.Raise(new Box(2, 2, 4, 10), AccessibleFocusKind.Caret);
        Assert.Equal((60.0, 40.0, 80.0, 60.0), output.ZoomArea);
        zoom.Follow = ZoomFollow.All;
        focus.Raise(new Box(2, 2, 4, 10), AccessibleFocusKind.Caret);
        Assert.Equal((2.0, 2.0, 80.0, 60.0), output.ZoomArea);

        zoom.ZoomIn();
        Assert.Equal(2.5, zoom.Factor);
        for (var i = 0; i < 4; i++)
        {
            zoom.ZoomOut();
        }

        Assert.Equal(1.0, zoom.Factor);
        Assert.False(output.IsZoomed);
        zoom.Toggle();
        Assert.Equal(1.28, zoom.Factor);
        zoom.Toggle();
        Assert.False(output.IsZoomed);
    }

    [Fact]
    public void A_disposed_output_ignores_the_cursor_and_zoom_calls_that_teardown_still_makes()
    {
        using var host = new CompositorTestHost();
        var output = new SceneOutput(host.Scene, host.Output);
        var cursor = new MemoryBuffer(4, 4, DrmFormat.Argb8888);
        try
        {
            output.SetZoom(2, 80, 60);
            output.SetSoftwareCursor(cursor, 0, 0);
            output.Dispose();
            output.SetSoftwareCursor(cursor, 1, 1);
            output.MoveSoftwareCursor(10, 10);
            output.SetZoom(3, 40, 40);
            Assert.Equal(2, output.ZoomFactor);
        }
        finally
        {
            cursor.Destroy();
        }
    }

    [Fact]
    public void A_zoom_view_follows_the_controller_and_projects_like_a_scene_output()
    {
        using var host = new CompositorTestHost();
        var output = host.SceneOutput;
        var pointer = new LayoutPointer(host.Layout);
        pointer.Warp(40, 30);
        using var zoom = new OutputZoom(host.Scene, pointer);
        var view = new ZoomView(host.Scene, host.Output);
        var changes = 0;
        view.Changed += () => changes++;
        Assert.Contains(view, host.Scene.Zoomables);

        zoom.Toggle();
        Assert.Equal(2.0, view.ZoomFactor);
        Assert.Equal(1, changes);
        Assert.Equal(output.ZoomArea, view.ZoomArea);
        Assert.Equal(output.Projection, view.Projection);

        view.Dispose();
        Assert.DoesNotContain(view, host.Scene.Zoomables);
        zoom.Toggle();
        Assert.Equal(2.0, view.ZoomFactor);
    }

    private readonly struct BufferGuard(MemoryBuffer buffer) : IDisposable
    {
        public MemoryBuffer Buffer => buffer;

        public void Dispose() => buffer.Destroy();
    }
}
