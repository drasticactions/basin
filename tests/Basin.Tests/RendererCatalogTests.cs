using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public sealed class RendererCatalogTests
{
    [Fact]
    public void Every_advertised_name_builds()
    {
        var built = 0;
        foreach (var name in RendererCatalog.Names)
        {
            if (!CompositorTestHost.IsRunnable(name))
            {
                continue;
            }

            var stack = RendererCatalog.Create(name, CompositorTestHost.RenderNodePath);
            try
            {
                Assert.NotNull(stack.Renderer);

                Assert.Equal(stack.DeviceAllocator is null, stack.NeedsMappableTarget);
                if (stack.DeviceAllocator is not null)
                {
                    Assert.NotNull(stack.Renderer.Device);
                }

                built++;
            }
            finally
            {
                stack.DeviceAllocator?.Dispose();
                stack.Renderer.Dispose();
            }
        }

        Assert.True(built >= 2, "the two CPU rows build anywhere");
    }

    [Fact]
    public void An_unknown_name_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RendererCatalog.Create("software", CompositorTestHost.RenderNodePath));
    }

    [Fact]
    public void Gpu_rows_are_exactly_the_ones_with_a_device()
    {
        Assert.SkipWhen(!File.Exists(CompositorTestHost.RenderNodePath), "no render node");

        foreach (var name in RendererCatalog.Names)
        {
            if (!CompositorTestHost.IsRunnable(name))
            {
                continue;
            }

            var stack = RendererCatalog.Create(name, CompositorTestHost.RenderNodePath);
            try
            {
                Assert.Equal(RendererCatalog.NeedsGpu(name), stack.Renderer.Device is not null);
            }
            finally
            {
                stack.DeviceAllocator?.Dispose();
                stack.Renderer.Dispose();
            }
        }
    }
}
