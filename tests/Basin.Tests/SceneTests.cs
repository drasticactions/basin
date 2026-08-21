using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class SceneTests : IDisposable
{
    private readonly PixmanRenderer _renderer;
    private readonly Scene.Scene _scene;
    private readonly MemoryBuffer _target;

    public SceneTests()
    {
        BasinCounters.Reset();
        _renderer = new PixmanRenderer();
        _scene = new Scene.Scene();
        _target = new MemoryBuffer(100, 100, DrmFormat.Xrgb8888);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _scene.Root.Destroy();
        _target.Destroy();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    private uint Render(int x, int y)
    {
        _scene.Render(_renderer, _target, RenderColor.Black);
        Assert.True(_target.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            unsafe
            {
                return ((uint*)(view.Data + y * view.Stride))[x] | 0xFF000000u;
            }
        }
        finally
        {
            _target.EndDataAccess();
        }
    }

    [Fact]
    public void Later_siblings_paint_on_top()
    {
        var red = new SceneRect(_scene.Root, 50, 50, new RenderColor(1, 0, 0, 1));
        var blue = new SceneRect(_scene.Root, 50, 50, new RenderColor(0, 0, 1, 1));
        blue.SetPosition(25, 25);

        Assert.Equal(0xFF0000FFu, Render(30, 30));

        red.RaiseToTop();
        Assert.Equal(0xFFFF0000u, Render(30, 30));

        red.LowerToBottom();
        Assert.Equal(0xFF0000FFu, Render(30, 30));

        blue.PlaceBelow(red);
        Assert.Equal(0xFFFF0000u, Render(30, 30));
    }

    [Fact]
    public void Disabled_nodes_do_not_render()
    {
        var rect = new SceneRect(_scene.Root, 50, 50, new RenderColor(1, 0, 0, 1));
        Assert.Equal(0xFFFF0000u, Render(10, 10));

        rect.Enabled = false;
        Assert.Equal(0xFF000000u, Render(10, 10));
    }

    [Fact]
    public void Tree_positions_compose()
    {
        var tree = new SceneTree(_scene.Root);
        tree.SetPosition(20, 20);
        var rect = new SceneRect(tree, 10, 10, new RenderColor(0, 1, 0, 1));
        rect.SetPosition(5, 5);

        Assert.Equal(0xFF00FF00u, Render(26, 26));
        Assert.Equal(0xFF000000u, Render(24, 24));
    }

    [Fact]
    public void Reparent_moves_a_node_between_trees()
    {
        var treeA = new SceneTree(_scene.Root);
        var treeB = new SceneTree(_scene.Root);
        treeB.SetPosition(50, 0);
        var rect = new SceneRect(treeA, 10, 10, new RenderColor(1, 1, 0, 1));

        Assert.Equal(0xFFFFFF00u, Render(5, 5));

        rect.Reparent(treeB);
        Assert.Equal(0xFF000000u, Render(5, 5));
        Assert.Equal(0xFFFFFF00u, Render(55, 5));
    }

    [Fact]
    public void The_oracle_waits_on_the_acquire_fence_of_what_it_samples()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the fence stands in for a sync fd, which is a Linux object");
        var recorder = new FenceRecordingRenderer(_renderer);
        var buffer = new MemoryBuffer(10, 10, DrmFormat.Xrgb8888);
        var node = new SceneBuffer(_scene.Root);
        node.SetBuffer(buffer);

        _scene.Render(recorder, _target, RenderColor.Black);
        Assert.Equal(-1, recorder.LastWaitFence);

        var fence = memfd_create("scene-oracle-fence", 0);
        Assert.True(fence >= 0);
        node.AcquireFenceFd = fence;

        _scene.Render(recorder, _target, RenderColor.Black);
        Assert.Equal(fence, recorder.LastWaitFence);

        node.AcquireFenceFd = -1;
        _scene.Render(recorder, _target, RenderColor.Black);
        Assert.Equal(-1, recorder.LastWaitFence);

        close(fence);
        node.Destroy();
        buffer.Destroy();
    }

    private sealed class FenceRecordingRenderer(IRenderer inner) : IRenderer
    {
        public int LastWaitFence { get; private set; } = -1;

        public ITexture? ImportTexture(IBuffer buffer) => inner.ImportTexture(buffer);

        public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
        {
            LastWaitFence = options.WaitFenceFd;
            return inner.BeginBufferPass(target, options);
        }

        public void Dispose()
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void A_buffer_destroyed_before_its_first_draw_still_drops_its_texture()
    {
        var buffer = new MemoryBuffer(10, 10, DrmFormat.Xrgb8888);
        var node = new SceneBuffer(_scene.Root);
        node.SetBuffer(buffer);
        buffer.Destroy();

        _scene.Render(_renderer, _target, RenderColor.Black);
        Assert.NotNull(_scene.PeekTexture(buffer));

        node.Destroy();
        Assert.Null(_scene.PeekTexture(buffer));
    }

    [Fact]
    public void A_buffer_destroyed_after_its_first_draw_drops_its_texture()
    {
        var buffer = new MemoryBuffer(10, 10, DrmFormat.Xrgb8888);
        var node = new SceneBuffer(_scene.Root);
        node.SetBuffer(buffer);

        _scene.Render(_renderer, _target, RenderColor.Black);
        Assert.NotNull(_scene.PeekTexture(buffer));

        buffer.Destroy();
        Assert.Null(_scene.PeekTexture(buffer));

        node.Destroy();
    }

    [Fact]
    public void Buffer_nodes_lock_and_release_their_buffer()
    {
        var buffer = new MemoryBuffer(10, 10, DrmFormat.Xrgb8888);
        var node = new SceneBuffer(_scene.Root);
        node.SetBuffer(buffer);
        Assert.Equal(1, buffer.LockCount);

        node.SetBuffer(null);
        Assert.Equal(0, buffer.LockCount);

        node.SetBuffer(buffer);
        node.Destroy();
        Assert.Equal(0, buffer.LockCount);
        buffer.Destroy();
    }
}
