using Basin.Capabilities;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class UISurfaceNodeTests
{
    [Fact]
    public void Publishing_sizes_the_node_from_the_surface()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var surface = (FalsifierUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 64,
            Height = 32,
            Scale = 2.0,
        })!;

        var node = new UISurfaceNode(scene.Root, surface) { PreciseDamage = true };
        Assert.False(node.Node.Enabled);

        Paint(surface);
        Assert.True(node.Publish());
        Assert.True(node.Node.Enabled);
        Assert.True(node.Node.PreciseDamage);
        Assert.Equal(64, node.Node.DestinationWidth);
        Assert.Equal(32, node.Node.DestinationHeight);
        Assert.Equal(128, node.Node.SourceBox.Width);
        Assert.Equal(64, node.Node.SourceBox.Height);
        Assert.NotNull(node.Node.Buffer);

        node.Dispose();
        surface.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void Damage_from_the_surface_publishes_without_a_caller()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var surface = (FalsifierUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 16,
            Height = 16,
            Scale = 1.0,
        })!;

        var node = new UISurfaceNode(scene.Root, surface);
        var watcher = new UISurfaceNode(scene.Root, surface);
        Paint(surface);
        Assert.True(node.Publish());
        Assert.Null(watcher.Node.Buffer);

        Paint(surface);
        Assert.NotNull(watcher.Node.Buffer);
        Assert.True(watcher.Node.Enabled);

        watcher.Dispose();
        node.Dispose();
        surface.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_host_owned_node_creates_its_surface_on_the_first_configure()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var node = new UISurfaceNode(scene.Root, host) { AutoEnable = false, InputEnabled = false };

        Assert.Null(node.Surface);
        Assert.False(node.Configure(0, 32, 1.0));
        Assert.Null(node.Surface);

        Assert.True(node.Configure(48, 32, 1.0));
        Assert.NotNull(node.Surface);
        Assert.Equal(48, node.Width);
        Assert.Equal(32, node.Height);
        Assert.False(node.Node.InputEnabled);

        var created = node.Surface;
        Assert.True(node.Configure(64, 32, 1.0));
        Assert.Same(created, node.Surface);

        node.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_declined_surface_faults_the_node_once()
    {
        var scene = new Scene.Scene();
        var host = new DecliningUIHost();
        var node = new UISurfaceNode(scene.Root, host);
        Exception? reported = null;
        node.Faulted += error => reported = error;

        Assert.False(node.Configure(32, 32, 1.0));
        Assert.True(node.IsFaulted);
        Assert.NotNull(reported);
        Assert.Equal(1, host.Attempts);

        Assert.False(node.Configure(32, 32, 1.0));
        Assert.Equal(1, host.Attempts);

        node.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void The_index_maps_the_scene_node_and_forgets_it_on_dispose()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var surface = (FalsifierUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 20,
            Height = 20,
            Scale = 1.0,
        })!;

        var index = new UISurfaceIndex();
        var node = new UISurfaceNode(scene.Root, surface, index);
        Assert.Same(surface, index.SurfaceOf(node.Node));
        Assert.Same(node, index.NodeOf(node.Node));

        var sceneNode = node.Node;
        node.Dispose();
        Assert.Null(index.SurfaceOf(sceneNode));
        Assert.Equal(0, index.Count);

        surface.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_host_owned_node_disposes_the_surface_it_created()
    {
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var node = new UISurfaceNode(scene.Root, host);
        Assert.True(node.Configure(24, 24, 1.0));
        var surface = (FalsifierUISurface)node.Surface!;

        node.Dispose();
        Assert.False(surface.AcceptsInputAt(1, 1));
        scene.Root.Destroy();
    }

    [Fact]
    public void A_frame_with_an_acquire_fence_hands_it_to_the_scene_node()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "sync_file fds are a Linux object");
        var scene = new Scene.Scene();
        using var host = new FalsifierUIHost();
        var surface = (FalsifierUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 16,
            Height = 16,
            Scale = 1.0,
        })!;

        var node = new UISurfaceNode(scene.Root, surface);
        Assert.Equal(-1, node.Node.AcquireFenceFd);

        var first = OpenFence();
        surface.NextFenceFd = first;
        Paint(surface);
        Assert.True(node.Publish());
        Assert.Equal(first, node.Node.AcquireFenceFd);
        Assert.True(IsOpen(first));

        var second = OpenFence();
        surface.NextFenceFd = second;
        Paint(surface);
        Assert.Equal(second, node.Node.AcquireFenceFd);
        Assert.False(IsOpen(first), "the frame it replaced owned the fence it carried");

        node.Dispose();
        Assert.Equal(-1, node.Node.AcquireFenceFd);
        Assert.False(IsOpen(second));
        surface.Dispose();
        scene.Root.Destroy();
    }

    [Fact]
    public void A_frame_that_carries_no_fence_closes_none()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "sync_file fds are a Linux object");
        var empty = default(UIFrame);
        Assert.Equal(-1, empty.AcquireFenceFd);
        empty.Dispose();
        Assert.True(IsOpen(0), "an unfenced frame must not close descriptor zero");
    }

    private static int OpenFence()
    {
        var fd = memfd_create("basin-test-fence", 0);
        Assert.True(fd >= 0);
        return fd;
    }

    private static bool IsOpen(int fd) => File.Exists($"/proc/self/fd/{fd}");

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    private static void Paint(FalsifierUISurface surface)
    {
        surface.BeginPixels();
        surface.EndPixels();
    }

    private sealed class DecliningUIHost : IUIHost
    {
        public UITargetKind Produces => UITargetKind.Memory;

        public long? NextDueMillis => null;

        public int Attempts { get; private set; }

        public event Action? WakeupRequested
        {
            add
            {
            }

            remove
            {
            }
        }

        public IUISurface? CreateSurface(in UISurfaceOptions options)
        {
            Attempts++;
            return null;
        }

        public void Pump()
        {
        }

        public void Dispose()
        {
        }
    }
}
