using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class OutputAspectTests
{
    [Fact]
    public void The_content_box_letterboxes_and_shrinks_the_logical_size()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetAspectRatio(1.0)));

        Assert.Equal(1.0, host.Output.AspectRatio);
        Assert.Equal(new Box(20, 0, 120, 120), host.Output.ContentBox());
        Assert.Equal((120, 120), host.Output.LogicalSize());

        state.Clear();
        Assert.True(host.Output.Commit(state.SetAspectRatio(0)));
        Assert.Equal(new Box(0, 0, 160, 120), host.Output.ContentBox());
        Assert.Equal((160, 120), host.Output.LogicalSize());
    }

    [Fact]
    public void A_transform_letterboxes_in_the_rotated_orientation()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetTransform(OutputTransform.Rotate90).SetAspectRatio(1.0)));

        Assert.Equal(new Box(0, 20, 120, 120), host.Output.ContentBox());
        Assert.Equal((120, 120), host.Output.LogicalSize());
    }

    [Fact]
    public void The_scene_composes_into_the_content_box_with_bars_around_it()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetAspectRatio(1.0)));

        var (width, height) = host.Output.LogicalSize();
        _ = new SceneRect(host.Scene.Root, width, height, new RenderColor(1f, 0f, 0f, 1f));

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        state.Clear();
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        int Channel(int x, int y, int c) => rgba[(((y * 160) + x) * 4) + c];
        Assert.True(Channel(80, 60, 0) > 200, "the scene fills the content box");
        Assert.True(Channel(25, 60, 0) > 200, "the content box starts at x=20");
        Assert.True(Channel(10, 60, 0) < 30 && Channel(10, 60, 1) < 30, "the left bar stays background");
        Assert.True(Channel(150, 60, 0) < 30, "the right bar stays background");
        target.Destroy();
    }

    [Fact]
    public void The_layout_advertises_the_effective_size()
    {
        using var host = new CompositorTestHost();
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetAspectRatio(4.0 / 3.0)));

        var box = host.Layout.BoxOf(host.Output);
        Assert.Equal(160, box.Width);
        Assert.Equal(120, box.Height);

        state.Clear();
        Assert.True(host.Output.Commit(state.SetAspectRatio(1.0)));
        host.Layout.Add(host.Output, 0, 0);
        box = host.Layout.BoxOf(host.Output);
        Assert.Equal(120, box.Width);
        Assert.Equal(120, box.Height);
    }
}
