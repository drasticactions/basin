using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneTransformTests
{
    private static Basin.Scene.Scene NewScene() => new();

    private static bool Covers(Box outer, Box inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    [Fact]
    public void Hit_test_maps_through_a_rotation()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        transform.SetPosition(100, 100);
        var rect = new SceneRect(transform, 60, 20, new RenderColor(1f, 0f, 0f, 1f));

        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 2, 0, 0);

        var hit = scene.NodeAt(100 - 10, 100 + 30);
        Assert.NotNull(hit);
        Assert.Same(rect, hit!.Value.Node);
        Assert.Equal(30, hit.Value.X, 6);
        Assert.Equal(10, hit.Value.Y, 6);

        Assert.Null(scene.NodeAt(130, 110));
    }

    [Fact]
    public void Degenerate_transform_is_not_hittable()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        _ = new SceneRect(transform, 60, 20, new RenderColor(1f, 0f, 0f, 1f));
        transform.Matrix = RenderTransform.Scale(1, 0);

        Assert.Null(scene.NodeAt(30, 0));
        Assert.Null(scene.NodeAt(30, 10));
    }

    [Fact]
    public void Identity_transform_hits_exactly_like_a_tree()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        transform.SetPosition(20, 10);
        var rect = new SceneRect(transform, 40, 30, new RenderColor(1f, 0f, 0f, 1f));

        var hit = scene.NodeAt(25, 15);
        Assert.NotNull(hit);
        Assert.Same(rect, hit!.Value.Node);
        Assert.Equal(5, hit.Value.X, 6);
        Assert.Equal(5, hit.Value.Y, 6);
    }

    [Fact]
    public void Clip_box_applies_in_pre_transform_space()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        _ = new SceneRect(transform, 60, 60, new RenderColor(1f, 0f, 0f, 1f));
        transform.ClipBox = new Box(0, 0, 30, 60);
        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 2, 0, 0);

        Assert.NotNull(scene.NodeAt(-10, 10));
        Assert.Null(scene.NodeAt(-10, 40));
    }

    [Fact]
    public void Touch_points_map_through_the_chain()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        transform.SetPosition(50, 50);
        var rect = new SceneRect(transform, 40, 40, new RenderColor(1f, 1f, 1f, 1f));
        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 2, 0, 0);

        var touch = new TouchPoints();
        touch.Down(1, 40, 60, rect);
        Assert.True(touch.TryMotion(1, 40, 70, out var localX, out var localY));
        Assert.Equal(20, localX, 6);
        Assert.Equal(10, localY, 6);
    }

    [Fact]
    public void Matrix_change_damages_old_and_new_hull()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        transform.SetPosition(100, 100);
        _ = new SceneRect(transform, 40, 20, new RenderColor(1f, 0f, 0f, 1f));

        var boxes = new List<Box>();
        scene.Damaged += (_, box) => boxes.Add(box);
        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 2, 0, 0);

        Assert.Contains(boxes, box => Covers(box, new Box(100, 100, 40, 20)));
        Assert.Contains(boxes, box => Covers(box, new Box(80, 100, 20, 40)));
    }

    [Fact]
    public void Child_damage_maps_through_the_ancestor_transform()
    {
        var scene = NewScene();
        var transform = new SceneTransform(scene.Root);
        transform.SetPosition(100, 100);
        var rect = new SceneRect(transform, 40, 20, new RenderColor(1f, 0f, 0f, 1f));
        transform.Matrix = RenderTransform.RotationAbout(Math.PI / 2, 0, 0);

        var boxes = new List<Box>();
        scene.Damaged += (_, box) => boxes.Add(box);
        rect.Color = new RenderColor(0f, 1f, 0f, 1f);

        Assert.Contains(boxes, box => Covers(box, new Box(80, 100, 20, 40)));
        Assert.DoesNotContain(boxes, box => box.Equals(new Box(100, 100, 40, 20)));
    }

    [Fact]
    public void Group_alpha_scales_rect_draws()
    {
        using var host = new CompositorTestHost();
        var transform = new SceneTransform(host.Scene.Root);
        transform.SetPosition(10, 10);
        _ = new SceneRect(transform, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        transform.Alpha = 0.5f;

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        var index = ((15 * host.Target.Width) + 15) * 4;
        Assert.InRange(rgba[index], 120, 136);
    }

    [Fact]
    public void Degenerate_matrix_collects_nothing()
    {
        using var host = new CompositorTestHost();
        var transform = new SceneTransform(host.Scene.Root);
        _ = new SceneRect(transform, 20, 20, new RenderColor(1f, 0f, 0f, 1f));
        transform.Matrix = RenderTransform.Scale(0, 1);

        host.RenderFrame();
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(host.Target);
        var corner = ((70 * host.Target.Width) + 90) * 4;
        Assert.Equal(rgba[corner], rgba[((10 * host.Target.Width) + 10) * 4]);
    }
}
