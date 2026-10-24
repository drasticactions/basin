using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class UIStripFrameTests
{
    private static readonly UIStripFrameMetrics Metrics = new(Margin: 10, Border: 4, Titlebar: 20);

    [Fact]
    public void Four_strips_surround_the_content_with_the_shadow_margin_outside()
    {
        BasinCounters.Reset();
        var scene = new Scene.Scene();
        var tree = new SceneTree(scene.Root);
        tree.SetPosition(100, 50);
        using var host = new FalsifierUIHost();
        var filled = new List<UIFrameStrip>();
        var frame = new UIStripFrame(host, tree, Metrics, (strip, _) => filled.Add(strip));

        Assert.True(frame.Update(100, 50, 200, 100, 1.0));
        Assert.Equal([UIFrameStrip.Title, UIFrameStrip.Left, UIFrameStrip.Right, UIFrameStrip.Bottom], filled);
        Assert.Equal(new Box(86, 20, 228, 144), frame.OuterBox);
        Assert.Equal(new UISurfaceSize(228, 30, 1.0), frame.SurfaceOf(UIFrameStrip.Title)!.Size);
        Assert.Equal(new UISurfaceSize(14, 100, 1.0), frame.SurfaceOf(UIFrameStrip.Left)!.Size);
        Assert.Equal(new UISurfaceSize(228, 14, 1.0), frame.SurfaceOf(UIFrameStrip.Bottom)!.Size);
        var right = (FalsifierUISurface)frame.SurfaceOf(UIFrameStrip.Right)!;
        Assert.Equal((300.0, 50.0), (right.PositionX, right.PositionY));
        Assert.True(frame.OwnsSurface(right));

        Assert.False(frame.AcceptsInputAt(90, 25));
        Assert.True(frame.AcceptsInputAt(150, 35));
        Assert.False(frame.AcceptsInputAt(150, 100));
        Assert.True(frame.AcceptsInputAt(97, 100));
        Assert.True(frame.HitsTitlebar(150, 35));
        Assert.False(frame.HitsTitlebar(150, 55));
        Assert.Equal(FrameEdges.TopLeft, frame.EdgeAt(97, 31));
        Assert.Equal(FrameEdges.BottomRight, frame.EdgeAt(302, 152));
        Assert.Equal(FrameEdges.None, frame.EdgeAt(150, 40));

        Assert.True(frame.Update(120, 50, 300, 100, 1.0));
        Assert.Equal(4, filled.Count);
        Assert.Equal(new UISurfaceSize(328, 30, 1.0), frame.SurfaceOf(UIFrameStrip.Title)!.Size);

        frame.Dispose();
        frame.Dispose();
        Assert.False(frame.Update(0, 0, 10, 10, 1.0));
        scene.Root.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [Fact]
    public void An_empty_content_box_places_nothing()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        using var frame = new UIStripFrame(host, scene.Root, Metrics, (_, _) => { });
        Assert.False(frame.Update(0, 0, 0, 100, 1.0));
        Assert.Null(frame.SurfaceOf(UIFrameStrip.Title));
        scene.Root.Destroy();
    }
}
