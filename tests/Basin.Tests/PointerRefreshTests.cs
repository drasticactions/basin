using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneStructureChangedTests
{
    [Fact]
    public void Every_structural_mutator_reports_a_change()
    {
        var scene = new Scene.Scene();
        var node = new SceneRect(scene.Root, 20, 20, new RenderColor(1f, 1f, 1f, 1f));
        var sibling = new SceneRect(scene.Root, 20, 20, new RenderColor(1f, 1f, 1f, 1f));
        var count = 0;
        scene.StructureChanged += () => count++;

        node.SetPosition(5, 5);
        Assert.True(count > 0, "a move reports a change");

        count = 0;
        node.RaiseToTop();
        Assert.True(count > 0, "a raise reports a change");

        count = 0;
        node.LowerToBottom();
        Assert.True(count > 0, "a lower reports a change");

        count = 0;
        node.PlaceAbove(sibling);
        Assert.True(count > 0, "a restack reports a change");

        count = 0;
        node.Enabled = false;
        Assert.True(count > 0, "an unmap reports a change");

        count = 0;
        node.Enabled = true;
        Assert.True(count > 0, "a map reports a change");

        count = 0;
        var group = new SceneTree(scene.Root);
        node.Reparent(group);
        Assert.True(count > 0, "a reparent reports a change");

        count = 0;
        node.Destroy();
        Assert.True(count > 0, "a destroy reports a change");

        scene.Root.Destroy();
    }

    [Fact]
    public void A_transform_step_reports_a_change()
    {
        var scene = new Scene.Scene();
        var transform = new SceneTransform(scene.Root);
        _ = new SceneRect(transform, 20, 20, new RenderColor(1f, 1f, 1f, 1f));
        var count = 0;
        scene.StructureChanged += () => count++;

        transform.Matrix = RenderTransform.Scale(2f, 2f);
        Assert.True(count > 0);

        scene.Root.Destroy();
    }

    [Fact]
    public void A_same_size_commit_reports_no_change()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xFFFFFFFF));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        surface.Commit();
        host.PumpToServer();

        var count = 0;
        host.Scene.StructureChanged += () => count++;

        for (var i = 0; i < 5; i++)
        {
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 32, 32);
            surface.Commit();
            host.PumpToServer();
        }

        Assert.Equal(0, count);

        surface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_resize_reports_a_change()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var small = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xFFFFFFFF));
        surface.Attach(small.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        surface.Commit();
        host.PumpToServer();

        var count = 0;
        host.Scene.StructureChanged += () => count++;

        var large = host.Client.CreateBuffer(64, 64, Fill.Solid(64, 64, 0xFFFFFFFF));
        surface.Attach(large.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 64);
        surface.Commit();
        host.PumpToServer();

        Assert.True(count > 0);

        surface.Dispose();
        host.PumpToServer();
    }
}

public sealed class PointerRefreshTests
{
    [Fact]
    public void Many_changes_coalesce_into_one_refresh()
    {
        using var host = new CompositorTestHost();
        var refreshes = 0;
        using var refresh = new PointerRefresh(host.Scene, host.Loop, () => refreshes++);
        var node = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 1f, 1f, 1f));

        node.SetPosition(1, 1);
        node.SetPosition(2, 2);
        node.RaiseToTop();
        node.Enabled = false;
        Assert.Equal(0, refreshes);

        host.Loop.DispatchIdle();
        Assert.Equal(1, refreshes);

        host.Loop.DispatchIdle();
        Assert.Equal(1, refreshes);

        node.Destroy();
    }

    [Fact]
    public void A_refresh_that_changes_the_scene_does_not_arm_another()
    {
        using var host = new CompositorTestHost();
        var node = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 1f, 1f, 1f));
        var refreshes = 0;
        using var refresh = new PointerRefresh(
            host.Scene,
            host.Loop,
            () =>
            {
                refreshes++;
                node.SetPosition(refreshes, refreshes);
            });

        node.SetPosition(9, 9);
        host.Loop.DispatchIdle();
        Assert.Equal(1, refreshes);

        host.Loop.DispatchIdle();
        Assert.Equal(1, refreshes);

        node.Destroy();
    }

    [Fact]
    public void A_disposed_refresh_stops_listening()
    {
        using var host = new CompositorTestHost();
        var refreshes = 0;
        var refresh = new PointerRefresh(host.Scene, host.Loop, () => refreshes++);
        var node = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(1f, 1f, 1f, 1f));

        node.SetPosition(2, 2);
        refresh.Dispose();
        host.Loop.DispatchIdle();
        Assert.Equal(0, refreshes);

        node.SetPosition(4, 4);
        host.Loop.DispatchIdle();
        Assert.Equal(0, refreshes);

        node.Destroy();
    }
}
