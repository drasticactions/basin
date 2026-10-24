using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public sealed class RendererFloorTests
{
    [Fact]
    public void A_gl_floor_throws_where_the_default_falls_to_pixman()
    {
        var name = "gl";
        var falls = new List<RendererFallback>();
        var stack = RendererCatalog.CreateWithFallback(ref name, "/dev/dri/no-such-node", falls.Add);
        Assert.Equal("pixman", name);
        Release(stack);

        name = "gl";
        Assert.ThrowsAny<Exception>(() => RendererCatalog.CreateWithFallback(ref name, "/dev/dri/no-such-node", floor: "gl"));
    }

    [Fact]
    public void A_gl_floor_with_no_render_node_throws_and_reports_nothing()
    {
        var name = "gl";
        var falls = new List<RendererFallback>();
        var error = Assert.Throws<InvalidOperationException>(
            () => RendererCatalog.CreateWithFallback(ref name, null, falls.Add, floor: "gl"));
        Assert.Contains("no render node", error.Message);
        Assert.Empty(falls);

        name = "gl";
        var stack = RendererCatalog.CreateWithFallback(ref name, null, falls.Add);
        Assert.Equal("pixman", name);
        Assert.Equal(new RendererFallback("gl", "pixman", null), Assert.Single(falls));
        Release(stack);
    }

    [Fact]
    public void Pixman_has_no_world_renderer()
    {
        var name = "pixman";
        var stack = RendererCatalog.CreateWithFallback(ref name, null);
        Assert.Null(WorldRenderers.For(stack.Renderer, 8, out var declined));
        Assert.NotNull(declined);
        Release(stack);
    }

    private static void Release(RenderStack stack)
    {
        stack.DeviceAllocator?.Dispose();
        stack.Renderer.Dispose();
    }
}
