using Basin.Renderers;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class NestedPassthroughTests
{
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, out Stat buffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Stat
    {
        public ulong Device;
        public ulong Inode;
        public ulong Links;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        public int Padding;
        public ulong Rdev;
        public long Size;
        public long BlockSize;
        public long Blocks;
        public long Atime;
        public long AtimeNs;
        public long Mtime;
        public long MtimeNs;
        public long Ctime;
        public long CtimeNs;
        public long Reserved0;
        public long Reserved1;
        public long Reserved2;
    }

    private static ulong InodeOf(IBuffer buffer) =>
        buffer.TryGetDmabuf(out var attributes) && fstat(attributes.Fds[0], out var stat) == 0 ? stat.Inode : 0;

    [Fact]
    public void A_fullscreen_client_dmabuf_reaches_the_parent_surface_without_a_composite()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        var stack = RendererCatalog.Create("gl", CompositorTestHost.RenderNodePath);
        try
        {
            Assert.SkipWhen(stack.DeviceAllocator is null, "the gl row built no device allocator");
            var allocator = stack.DeviceAllocator!;
            using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with
            {
                Dmabuf = true,
                DmabufFormats = allocator.Formats,
            });
            var output = host.CreateOutput();
            host.Pump();
            var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
            Assert.SkipWhen(modifiers.Length == 0, "the allocator offers no xrgb modifiers");
            var width = output.CurrentMode.Width;
            var height = output.CurrentMode.Height;
            var client = allocator.Allocate(width, height, DrmFormat.Xrgb8888, modifiers, BufferUse.Scanout);
            Assert.SkipWhen(client is null, "the allocator refused a scanout buffer");

            var scene = new Basin.Scene.Scene();
            using var sceneOutput = new SceneOutput(scene, output) { ScanoutEntryThreshold = 1 };
            using var swapchain = new Swapchain(allocator, width, height, DrmFormat.Xrgb8888, modifiers);
            using var state = new OutputState();
            var node = new SceneBuffer(scene.Root) { IsOpaque = true };
            node.SetBuffer(client!);

            Assert.True(sceneOutput.Commit(stack.Renderer, swapchain, state));
            host.Pump();
            Assert.True(sceneOutput.IsDirectScanout);
            Assert.Equal(0, sceneOutput.ComposedCommits);
            var parentBuffer = host.Parent.Invoke(() => host.Parent.Toplevels[0].Surface.Current.Buffer);
            Assert.NotNull(parentBuffer);
            Assert.Equal(InodeOf(client!), InodeOf(parentBuffer!));

            node.IsOpaque = false;
            new SceneRect(scene.Root, 8, 8, new RenderColor(1f, 0f, 0f, 1f));
            Assert.True(sceneOutput.Commit(stack.Renderer, swapchain, state));
            host.Pump();
            Assert.False(sceneOutput.IsDirectScanout);
            parentBuffer = host.Parent.Invoke(() => host.Parent.Toplevels[0].Surface.Current.Buffer);
            Assert.NotEqual(InodeOf(client!), InodeOf(parentBuffer!));

            node.Destroy();
            ((BufferBase)client!).Destroy();
            host.Pump();
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }

    private static (RenderStack Stack, IAllocator Allocator) Gl()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        var stack = RendererCatalog.Create("gl", CompositorTestHost.RenderNodePath);
        Assert.SkipWhen(stack.DeviceAllocator is null, "the gl row built no device allocator");
        return (stack, stack.DeviceAllocator!);
    }

    [Fact]
    public void A_passed_through_buffer_carries_its_description_to_the_parent_surface()
    {
        var (stack, allocator) = Gl();
        try
        {
            using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with
            {
                Dmabuf = true,
                DmabufFormats = allocator.Formats,
                ColorManagement = true,
            });
            var output = host.CreateOutput();
            host.Pump(8);
            var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
            var width = output.CurrentMode.Width;
            var height = output.CurrentMode.Height;
            var client = allocator.Allocate(width, height, DrmFormat.Xrgb8888, modifiers, BufferUse.Scanout);
            Assert.SkipWhen(client is null, "the allocator refused a scanout buffer");

            var hdr = new Basin.Capabilities.ImageDescription
            {
                PrimariesNamed = Basin.Capabilities.ColorPrimaries.Bt2020,
                TransferNamed = Basin.Capabilities.ColorTransferFunction.St2084Pq,
                MaxCll = 900,
            };
            var scene = new Basin.Scene.Scene();
            using var sceneOutput = new SceneOutput(scene, output) { ScanoutEntryThreshold = 1 };
            using var swapchain = new Swapchain(allocator, width, height, DrmFormat.Xrgb8888, modifiers);
            using var state = new OutputState();
            var node = new SceneBuffer(scene.Root) { IsOpaque = true, ColorDescription = hdr };
            node.SetBuffer(client!);

            for (var i = 0; i < 4 && output.PassthroughDescription is null; i++)
            {
                Assert.True(sceneOutput.Commit(stack.Renderer, swapchain, state));
                host.Pump();
            }

            Assert.True(sceneOutput.IsDirectScanout);
            Assert.NotNull(output.PassthroughDescription);
            var described = host.Parent.Invoke(() => host.Parent.Color!.DescriptionOf(host.Parent.Toplevels[0].Surface));
            Assert.Equal(Basin.Capabilities.ColorTransferFunction.St2084Pq, described.TransferNamed);
            Assert.Equal(900u, described.MaxCll);

            new SceneRect(scene.Root, 8, 8, new RenderColor(1f, 0f, 0f, 1f));
            Assert.True(sceneOutput.Commit(stack.Renderer, swapchain, state));
            host.Pump();
            Assert.Null(output.PassthroughDescription);
            for (var i = 0; i < 50; i++)
            {
                described = host.Parent.Invoke(() => host.Parent.Color!.DescriptionOf(host.Parent.Toplevels[0].Surface));
                if (described.TransferNamed == Basin.Capabilities.ImageDescription.SdrDefault.TransferNamed)
                {
                    break;
                }

                host.Pump();
            }

            Assert.Equal(Basin.Capabilities.ImageDescription.SdrDefault.TransferNamed, described.TransferNamed);

            node.Destroy();
            ((BufferBase)client!).Destroy();
            host.Pump();
        }
        finally
        {
            allocator.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Fact]
    public void A_format_the_parent_cannot_import_is_composited()
    {
        var (stack, allocator) = Gl();
        try
        {
            var parentFormats = new DrmFormatSet();
            parentFormats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
            using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with
            {
                Dmabuf = true,
                DmabufFormats = parentFormats,
            });
            var output = host.CreateOutput();
            host.Pump();
            Assert.False(output.CanScanout(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear, overlay: false));
            Assert.True(output.CanScanout(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear, overlay: false));
            Assert.False(output.CanScanout(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear, overlay: true));
        }
        finally
        {
            allocator.Dispose();
            stack.Renderer.Dispose();
        }
    }

    [Fact]
    public void The_host_toplevel_gets_the_icon_name()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with { ToplevelIcons = true });
        var output = host.CreateOutput();
        host.Pump();
        var pixels = new uint[16 * 16];
        Array.Fill(pixels, 0xFF3366CCu);
        Assert.True(output.SetIcon("org.example.game", pixels, 16));
        host.Pump();
        Assert.True(output.SetIcon(null));
        host.Pump();
        Assert.Equal(["org.example.game", null], host.Parent.Invoke(() => host.Parent.IconNames.ToList()));
    }
}
