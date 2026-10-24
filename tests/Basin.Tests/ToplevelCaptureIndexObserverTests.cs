using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public class ToplevelCaptureIndexObserverTests
{
    private sealed class StackChanges : IToplevelStackObserver
    {
        public int Count { get; private set; }

        public void OnToplevelStackChanged() => Count++;
    }

    [Fact]
    public void A_resolved_toplevel_lands_in_the_index_and_raises()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var changes = new StackChanges();
        stack.AddObserver(changes);
        var model = new TestToplevelModel();
        var tree = host.SurfaceScenes[0].Tree;

        model.AddObserver(new ToplevelCaptureIndexObserver(
            model, index, stack, s => ReferenceEquals(s, surface) ? new ToplevelCaptureTrees(tree, null) : null));
        var id = model.Add("title", "app", surface);

        Assert.True(index.TryGet(id, out var trees));
        Assert.Same(tree, trees.Content);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void An_unresolved_toplevel_fills_in_on_change()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var model = new TestToplevelModel();
        SceneTree? resolved = null;

        model.AddObserver(new ToplevelCaptureIndexObserver(
            model, index, stack, _ => resolved is null ? null : new ToplevelCaptureTrees(resolved, null)));
        var id = model.Add("title", "app", surface);
        Assert.False(index.TryGet(id, out _));

        resolved = host.SurfaceScenes[0].Tree;
        model.Retitle(id, "renamed");
        Assert.True(index.TryGet(id, out _));
    }

    [Fact]
    public void Removal_clears_the_index_and_always_raises()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var changes = new StackChanges();
        stack.AddObserver(changes);
        var model = new TestToplevelModel();
        var tree = host.SurfaceScenes[0].Tree;

        model.AddObserver(new ToplevelCaptureIndexObserver(
            model, index, stack, _ => new ToplevelCaptureTrees(tree, null)));
        var id = model.Add("title", "app", surface);
        Assert.Equal(1, changes.Count);

        model.Remove(id);
        Assert.False(index.TryGet(id, out _));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_change_with_a_new_tree_replaces_the_entry()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var changes = new StackChanges();
        stack.AddObserver(changes);
        var model = new TestToplevelModel();
        var first = new SceneTree(host.Scene.Root);
        SceneTree current = first;

        model.AddObserver(new ToplevelCaptureIndexObserver(
            model, index, stack, _ => new ToplevelCaptureTrees(current, null)));
        var id = model.Add("title", "app", surface);
        Assert.Equal(1, changes.Count);

        var second = new SceneTree(host.Scene.Root);
        first.Destroy();
        current = second;
        model.Retitle(id, "renamed");

        Assert.True(index.TryGet(id, out var trees));
        Assert.Same(second, trees.Content);
        Assert.Equal(1, changes.Count);
        Span<ulong> order = stackalloc ulong[4];
        Assert.Equal(1, stack.Enumerate(order));
        Assert.Equal(id, order[0]);
        second.Destroy();
    }

    [Fact]
    public void A_destroyed_tree_with_no_change_is_resolved_again_on_the_next_read()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var model = new TestToplevelModel();
        var first = new SceneTree(host.Scene.Root);
        SceneTree current = first;

        model.AddObserver(new ToplevelCaptureIndexObserver(
            model, index, stack, _ => new ToplevelCaptureTrees(current, null)));
        var id = model.Add("title", "app", surface);

        first.Destroy();
        Span<ulong> order = stackalloc ulong[4];
        Assert.Equal(0, stack.Enumerate(order));
        Assert.False(index.TryGet(id, out _));

        var second = new SceneTree(host.Scene.Root);
        current = second;
        Assert.Equal(1, stack.Enumerate(order));
        Assert.Equal(id, order[0]);
        Assert.True(index.TryGet(id, out var trees));
        Assert.Same(second, trees.Content);
        Assert.True(index.TryOwnerOf(second, out var owner));
        Assert.Equal(id, owner);
        second.Destroy();
    }

    [Fact]
    public void An_equal_result_raises_nothing()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = MapSurface(host);
        var index = new ToplevelSceneIndex();
        var stack = new SceneToplevelStack(host.Scene, index);
        var changes = new StackChanges();
        stack.AddObserver(changes);
        var model = new TestToplevelModel();
        var tree = host.SurfaceScenes[0].Tree;

        var observer = new ToplevelCaptureIndexObserver(
            model, index, stack, _ => new ToplevelCaptureTrees(tree, null));
        model.AddObserver(observer);
        var id = model.Add("title", "app", surface);
        Assert.Equal(1, changes.Count);

        model.Retitle(id, "renamed");
        observer.Refresh(id);
        Assert.True(index.TryGet(id, out _));
        Assert.Equal(1, changes.Count);
    }

    private static Surface MapSurface(CompositorTestHost host)
    {
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(16, 16, Fill.Solid(16, 16, 0xffff0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 16, 16);
        surface.Commit();
        host.PumpToServer();
        return host.SurfaceScenes[0].Surface;
    }
}
