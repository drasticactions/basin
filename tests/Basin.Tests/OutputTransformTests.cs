using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputTransformTests
{
    private static readonly OutputTransform[] All =
    [
        OutputTransform.Normal,
        OutputTransform.Rotate90,
        OutputTransform.Rotate180,
        OutputTransform.Rotate270,
        OutputTransform.Flipped,
        OutputTransform.Flipped90,
        OutputTransform.Flipped180,
        OutputTransform.Flipped270,
    ];

    [Fact]
    public void Only_the_quarter_turns_swap_axes()
    {
        Assert.False(OutputTransform.Normal.SwapsAxes());
        Assert.True(OutputTransform.Rotate90.SwapsAxes());
        Assert.False(OutputTransform.Rotate180.SwapsAxes());
        Assert.True(OutputTransform.Rotate270.SwapsAxes());
        Assert.False(OutputTransform.Flipped.SwapsAxes());
        Assert.True(OutputTransform.Flipped90.SwapsAxes());
        Assert.False(OutputTransform.Flipped180.SwapsAxes());
        Assert.True(OutputTransform.Flipped270.SwapsAxes());
    }

    [Fact]
    public void Every_transform_maps_the_whole_space_onto_the_whole_space()
    {
        foreach (var transform in All)
        {
            var mapped = transform.Apply(new Box(0, 0, 600, 800), 600, 800);
            var expected = transform.SwapsAxes() ? new Box(0, 0, 800, 600) : new Box(0, 0, 600, 800);
            Assert.Equal(expected, mapped);
        }
    }

    [Fact]
    public void Applying_a_transform_and_its_inverse_returns_the_box()
    {
        var box = new Box(11, 23, 40, 70);
        foreach (var transform in All)
        {
            var mapped = transform.Apply(box, 600, 800);
            var width = transform.SwapsAxes() ? 800 : 600;
            var height = transform.SwapsAxes() ? 600 : 800;
            Assert.Equal(box, transform.Invert().Apply(mapped, width, height));
        }
    }

    [Fact]
    public void A_quarter_turn_puts_the_logical_origin_on_the_far_edge()
    {
        Assert.Equal(new Box(780, 0, 20, 10), OutputTransform.Rotate90.Apply(new Box(0, 0, 10, 20), 600, 800));
        Assert.Equal(new Box(0, 590, 20, 10), OutputTransform.Rotate270.Apply(new Box(0, 0, 10, 20), 600, 800));
        Assert.Equal(new Box(590, 780, 10, 20), OutputTransform.Rotate180.Apply(new Box(0, 0, 10, 20), 600, 800));
    }

    [Fact]
    public void The_matrix_agrees_with_the_box_mapping()
    {
        var box = new Box(11, 23, 40, 70);
        foreach (var transform in All)
        {
            var matrix = transform.ToMatrix(600, 800);
            Assert.True(matrix.TryMapBounds(box, out var hull));
            Assert.Equal(transform.Apply(box, 600, 800), hull);
        }
    }

    [Fact]
    public void Composing_with_the_inverse_is_the_identity()
    {
        foreach (var transform in All)
        {
            Assert.Equal(OutputTransform.Normal, OutputTransforms.Compose(transform, transform.Invert()));
        }

        Assert.Equal(OutputTransform.Rotate180, OutputTransforms.Compose(OutputTransform.Rotate90, OutputTransform.Rotate90));
    }

    [Fact]
    public void A_quarter_turn_swaps_the_logical_size_and_reflows_the_layout()
    {
        using var host = new CompositorTestHost(800, 600);
        Assert.Equal((800, 600), host.Output.LogicalSize());

        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        Assert.Equal((600, 800), host.Output.LogicalSize());
        Assert.Equal(new Box(0, 0, 600, 800), host.Layout.BoxOf(host.Output));
    }

    [Fact]
    public void A_rotated_output_reports_the_swapped_logical_size_to_xdg_output()
    {
        using var host = new CompositorTestHost(800, 600);
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

        var sizes = new List<(int Width, int Height)>();
        var xdgOutput = proxy!.GetXdgOutput(client.Outputs[0]);
        xdgOutput.LogicalSize += (_, e) => sizes.Add((e.Width, e.Height));
        host.PumpToClient();
        Assert.Equal((800, 600), sizes[^1]);

        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));
        host.PumpToClient();

        Assert.Equal((600, 800), sizes[^1]);
        xdgOutput.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_rotated_output_draws_the_logical_origin_into_the_far_corner()
    {
        using var host = new CompositorTestHost(80, 60);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        _ = new SceneRect(host.Scene.Root, 10, 20, new RenderColor(1f, 0f, 0f, 1f));
        Assert.True(host.Scene.Render(host.Renderer, host.Target, new SceneRenderOptions
        {
            Background = RenderColor.Black,
            Projection = OutputProjection.For(host.Output),
        }));

        Assert.Equal(0xFFFF0000u, host.Pixel(0, 59));
        Assert.Equal(0xFFFF0000u, host.Pixel(19, 50));
        Assert.Equal(0xFF000000u, host.Pixel(19, 49));
        Assert.Equal(0xFF000000u, host.Pixel(0, 0));
    }

    [Fact]
    public void A_scene_output_commit_matches_the_naive_render_on_a_rotated_output()
    {
        using var host = new CompositorTestHost(80, 60);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate270)));

        _ = new SceneRect(host.Scene.Root, 17, 29, new RenderColor(0.2f, 0.7f, 0.4f, 1f));
        var oracle = new MemoryBuffer(80, 60, DrmFormat.Xrgb8888);
        var optimized = new MemoryBuffer(80, 60, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(host.Scene.Render(host.Renderer, oracle, new SceneRenderOptions
            {
                Background = RenderColor.Black,
                Projection = OutputProjection.For(host.Output),
            }));

            using var sceneOutput = new SceneOutput(host.Scene, host.Output);
            using var commitState = new OutputState();
            Assert.True(sceneOutput.Commit(host.Renderer, optimized, 0, commitState, new SceneCommitOptions
            {
                Background = RenderColor.Black,
            }));

            AssertSame(oracle, optimized);
        }
        finally
        {
            oracle.Destroy();
            optimized.Destroy();
        }
    }

    [Fact]
    public void Capturing_a_rotated_output_yields_the_framebuffer_it_scans_out()
    {
        using var host = new CompositorTestHost(80, 60);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        _ = new SceneRect(host.Scene.Root, 13, 21, new RenderColor(0f, 0.5f, 1f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Background = RenderColor.Black,
        };

        var source = CaptureSource.Output(host.Output);
        Assert.True(capture.TryDescribe(source, out var format));
        Assert.Equal((80, 60), (format.Width, format.Height));

        var shot = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        var oracle = new MemoryBuffer(80, 60, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(capture.Capture(source, default, shot));
            Assert.True(host.Scene.Render(host.Renderer, oracle, new SceneRenderOptions
            {
                Background = RenderColor.Black,
                Projection = OutputProjection.For(host.Output),
            }));

            AssertSame(oracle, shot);
        }
        finally
        {
            shot.Destroy();
            oracle.Destroy();
        }
    }

    [Fact]
    public void A_region_capture_of_a_rotated_output_is_the_transformed_crop()
    {
        using var host = new CompositorTestHost(80, 60);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        _ = new SceneRect(host.Scene.Root, 13, 21, new RenderColor(0f, 0.5f, 1f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Background = RenderColor.Black,
        };

        var projection = OutputProjection.For(host.Output);
        var crop = projection.Project(new Box(4, 7, 20, 15));
        Assert.Equal(new Box(7, 36, 15, 20), crop);

        var whole = new MemoryBuffer(80, 60, DrmFormat.Xrgb8888);
        var part = new MemoryBuffer(crop.Width, crop.Height, DrmFormat.Xrgb8888);
        try
        {
            var source = CaptureSource.Output(host.Output);
            Assert.True(capture.Capture(source, default, whole));
            Assert.True(capture.Capture(source, crop, part));
            AssertSame(whole, part, crop.X, crop.Y);
        }
        finally
        {
            whole.Destroy();
            part.Destroy();
        }
    }

    [Fact]
    public void Touch_and_pointer_coordinates_come_back_through_the_transform()
    {
        using var host = new CompositorTestHost(800, 600);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        var (x, y) = host.Layout.ToLayout(host.Output, 0, 599);
        Assert.Equal(1, x, 3);
        Assert.Equal(0, y, 3);

        var (cx, cy) = host.Layout.ToLayout(host.Output, 799, 0);
        Assert.Equal(600, cx, 3);
        Assert.Equal(799, cy, 3);

        var (nx, ny) = host.Layout.FromNormalized(host.Output, 0.0, 1.0);
        Assert.Equal(0, nx, 3);
        Assert.Equal(0, ny, 3);

        var (fx, fy) = host.Layout.FromNormalized(host.Output, 1.0, 1.0);
        Assert.Equal(0, fx, 3);
        Assert.Equal(800, fy, 3);

        var (bx, by) = host.Layout.FromNormalized(host.Output, 0.0, 0.0);
        Assert.Equal(600, bx, 3);
        Assert.Equal(0, by, 3);
    }

    [Fact]
    public void Applying_a_transform_reflows_the_outputs_beside_it()
    {
        using var host = new CompositorTestHost(800, 600);
        using var backend = new HeadlessBackend(host.Loop);
        var second = backend.CreateOutput(new OutputMode(800, 600, 60_000), manualFrameClock: true);
        using var secondState = new OutputState();
        second.Commit(secondState.SetEnabled(true).SetMode(new OutputMode(800, 600, 60_000)));

        var layout = new OutputLayout();
        layout.Add(host.Output, 0, 0);
        layout.Add(second, 800, 0);

        var configuration = new Basin.Capabilities.Defaults.LayoutOutputConfiguration(layout);
        var applied = 0;
        configuration.Applied += _ => applied++;
        configuration.Applied += _ => layout.ArrangeHorizontally([host.Output, second]);

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = host.Output,
                Enabled = true,
                Transform = OutputTransform.Rotate90,
            },
        ]));

        Assert.Equal(1, applied);
        Assert.Equal(new Box(0, 0, 600, 800), layout.BoxOf(host.Output));
        Assert.Equal(new Box(600, 0, 800, 600), layout.BoxOf(second));
        second.Destroy();
    }

    [Fact]
    public void The_software_cursor_lands_where_the_transform_puts_it()
    {
        using var host = new CompositorTestHost(160, 120);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var commitState = new OutputState();

        _ = new SceneRect(host.Scene.Root, 120, 160, new RenderColor(0, 0, 0, 1));

        var cursor = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        Assert.True(cursor.BeginDataAccess(BufferDataAccess.Write, out var view));
        unsafe
        {
            for (var y = 0; y < 8; y++)
            {
                var row = (uint*)(view.Data + (y * view.Stride));
                for (var x = 0; x < 8; x++)
                {
                    row[x] = 0xFFFFFFFF;
                }
            }
        }

        cursor.EndDataAccess();
        try
        {
            sceneOutput.SetSoftwareCursor(cursor, 0, 0);
            sceneOutput.MoveSoftwareCursor(20, 30);
            Assert.True(sceneOutput.Commit(host.Renderer, swapchain, commitState));

            var projection = OutputProjection.For(host.Output);
            var drawn = projection.MapPixels(new Box(20, 30, 8, 8));
            Assert.Equal(new Box(30, 92, 8, 8), drawn);
            AssertPixel(commitState.Buffer!, drawn.X + 1, drawn.Y + 1, 0xFFFFFFFF);
            AssertPixel(commitState.Buffer!, 20, 30, 0xFF000000);
        }
        finally
        {
            sceneOutput.SetSoftwareCursor(null, 0, 0);
            cursor.Destroy();
        }
    }

    [Fact]
    public void A_rotated_output_falls_back_from_the_cursor_plane()
    {
        using var host = new CompositorTestHost();
        using var output = new PlaneCursorOutput();
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new Basin.Desktop.CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");
        cursor.MoveTo(10, 10);

        Assert.False(cursor.IsSoftwareOn(output));
        Assert.NotNull(output.Cursor);

        using var state = new OutputState();
        Assert.True(output.Commit(state.SetTransform(OutputTransform.Rotate90)));

        Assert.True(cursor.IsSoftwareOn(output));
        Assert.Null(output.Cursor);
    }

    private sealed class PlaneCursorOutput : OutputBase, IHardwareCursor, IDisposable
    {
        public PlaneCursorOutput()
            : base("PLANE-1")
        {
            using var state = new OutputState();
            Commit(state.SetEnabled(true).SetMode(new OutputMode(160, 120, 60_000)));
        }

        public IBuffer? Cursor { get; private set; }

        public bool SetCursor(IBuffer? buffer, int hotspotX, int hotspotY)
        {
            Cursor = buffer;
            return true;
        }

        public void MoveCursor(int x, int y)
        {
        }

        public void Dispose() => Destroy();

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    private static void AssertPixel(IBuffer buffer, int x, int y, uint expected)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                var actual = *(uint*)(view.Data + (y * view.Stride) + (x * 4)) | 0xFF000000u;
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private static void AssertSame(MemoryBuffer expected, MemoryBuffer actual, int offsetX = 0, int offsetY = 0)
    {
        Assert.True(expected.BeginDataAccess(BufferDataAccess.Read, out var expectedView));
        try
        {
            Assert.True(actual.BeginDataAccess(BufferDataAccess.Read, out var actualView));
            try
            {
                unsafe
                {
                    for (var row = 0; row < actual.Height; row++)
                    {
                        var left = (uint*)(expectedView.Data + ((row + offsetY) * expectedView.Stride));
                        var right = (uint*)(actualView.Data + (row * actualView.Stride));
                        for (var column = 0; column < actual.Width; column++)
                        {
                            Assert.Equal(left[column + offsetX] | 0xFF000000u, right[column] | 0xFF000000u);
                        }
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
