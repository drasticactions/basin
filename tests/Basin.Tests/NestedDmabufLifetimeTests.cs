using Basin.Backend.Wayland;
using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public class NestedDmabufLifetimeTests
{
    [Fact]
    public void A_presented_buffer_destroyed_first_still_releases_its_lock()
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

            var buffer = allocator.Allocate(
                output.CurrentMode.Width, output.CurrentMode.Height, DrmFormat.Xrgb8888, modifiers, BufferUse.Scanout);
            Assert.SkipWhen(buffer is null, "the allocator refused a scanout buffer");
            Assert.True(buffer!.TryGetDmabuf(out _));

            using (var state = new OutputState())
            {
                Assert.True(output.Commit(state.SetBuffer(buffer)));
            }

            host.Pump();
            Assert.Equal(1, buffer.LockCount);

            ((BufferBase)buffer).Destroy();
            host.Pump();

            Assert.Equal(0, buffer.LockCount);
        }
        finally
        {
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }
}
