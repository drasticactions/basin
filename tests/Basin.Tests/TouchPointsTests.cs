using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public class TouchPointsTests
{
    [Fact]
    public void Motion_maps_into_the_down_node_even_after_the_finger_leaves_it()
    {
        var root = new SceneTree(null);
        var node = new SceneTree(root);
        node.SetPosition(10, 10);
        var points = new TouchPoints();

        points.Down(0, 12, 13, node);
        Assert.True(points.TryMotion(0, 50, 60, out var localX, out var localY));

        Assert.Equal(40, localX);
        Assert.Equal(50, localY);
        root.Destroy();
    }

    [Fact]
    public void Motion_follows_the_node_when_the_node_moves()
    {
        var root = new SceneTree(null);
        var node = new SceneTree(root);
        node.SetPosition(10, 10);
        var points = new TouchPoints();

        points.Down(0, 12, 13, node);
        node.SetPosition(20, 20);
        Assert.True(points.TryMotion(0, 25, 25, out var localX, out var localY));

        Assert.Equal(5, localX);
        Assert.Equal(5, localY);
        root.Destroy();
    }

    [Fact]
    public void A_destroyed_node_stops_receiving_and_up_reports_the_latch()
    {
        var root = new SceneTree(null);
        var node = new SceneTree(root);
        var points = new TouchPoints();

        points.Down(0, 1, 1, node);
        node.Destroy();

        Assert.False(points.TryMotion(0, 2, 2, out _, out _));
        Assert.True(points.Up(0));
        Assert.False(points.Up(0));
        root.Destroy();
    }

    [Fact]
    public void A_point_down_on_nothing_still_tracks_its_position()
    {
        var points = new TouchPoints();

        points.Down(3, 7, 8, null);
        Assert.False(points.TryMotion(3, 9, 10, out _, out _));

        Assert.True(points.TryGetPosition(3, out var x, out var y));
        Assert.Equal(9, x);
        Assert.Equal(10, y);
        Assert.False(points.Up(3));
        Assert.False(points.TryGetPosition(3, out _, out _));
    }

    [Fact]
    public void Cancel_forgets_every_point()
    {
        var root = new SceneTree(null);
        var node = new SceneTree(root);
        var points = new TouchPoints();

        points.Down(0, 1, 1, node);
        points.Down(1, 2, 2, null);
        points.Clear();

        Assert.False(points.TryMotion(0, 3, 3, out _, out _));
        Assert.False(points.TryGetPosition(1, out _, out _));
        root.Destroy();
    }

    [Fact]
    public void Scene_position_sums_the_ancestor_chain()
    {
        var root = new SceneTree(null);
        root.SetPosition(5, 5);
        var middle = new SceneTree(root);
        middle.SetPosition(3, 4);
        var leaf = new SceneTree(middle);
        leaf.SetPosition(1, 2);

        Assert.Equal((9, 11), leaf.ScenePosition);

        leaf.Reparent(root);
        Assert.Equal((6, 7), leaf.ScenePosition);
        root.Destroy();
    }
}
