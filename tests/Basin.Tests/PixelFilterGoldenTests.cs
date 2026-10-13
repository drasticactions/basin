using Xunit;

namespace Basin.Tests;

public sealed class PixelFilterGoldenTests
{
    public static TheoryData<string, int> RenderersAndScales()
    {
        var rows = new TheoryData<string, int>();
        foreach (var renderer in new[] { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" })
        {
            rows.Add(renderer, 3);
            rows.Add(renderer, 2);
        }

        return rows;
    }

    private const uint Light = 0xFFFFFFFFu;

    private const uint Dark = 0xFF2050C0u;

    [Theory]
    [MemberData(nameof(RenderersAndScales))]
    public void Nearest_pixel_and_linear_side_by_side(string renderer, int halves)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        using var host = new CompositorTestHost(renderer: renderer);
        static void Checker(nint data, int stride)
        {
            unsafe
            {
                for (var y = 0; y < 8; y++)
                {
                    var row = (uint*)(data + y * stride);
                    for (var x = 0; x < 8; x++)
                    {
                        row[x] = (((x / 2) + (y / 2)) & 1) == 0 ? Light : Dark;
                    }
                }
            }
        }

        var size = halves == 3 ? 24 : 12;
        var filters = new[] { TextureFilter.Nearest, TextureFilter.Pixel, TextureFilter.Linear };
        for (var i = 0; i < filters.Length; i++)
        {
            var surface = host.Client.Compositor.CreateSurface();
            var viewport = host.Client.Viewporter!.GetViewport(surface);
            viewport.SetDestination(size, size);
            var buffer = host.Client.CreateBuffer(8, 8, Checker);
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 8, 8);
            surface.Commit();
        }

        host.PumpToServer();
        for (var i = 0; i < filters.Length; i++)
        {
            host.SurfaceScenes[i].Tree.SetPosition(8 + (i * (size + 8)), 8);
            host.SurfaceScenes[i].Tree.Filter = filters[i];
        }

        host.RenderFrame();
        var cell = size / 4;
        for (var cellY = 0; cellY < 4; cellY++)
        {
            for (var cellX = 0; cellX < 4; cellX++)
            {
                var expected = ((cellX + cellY) & 1) == 0 ? Light : Dark;
                var cx = (cellX * cell) + (cell / 2);
                var cy = 8 + (cellY * cell) + (cell / 2);
                Assert.Equal(expected, host.Pixel(8 + cx, cy));
                Assert.Equal(expected, host.Pixel(8 + size + 8 + cx, cy));
            }
        }

        var pixelEdge = host.Pixel(8 + size + 8 + cell, 8 + (cell / 2));
        var nearestEdge = host.Pixel(8 + cell, 8 + (cell / 2));
        if (host.Renderer.SupportsFilter(TextureFilter.Pixel) && halves == 3)
        {
            Assert.NotEqual(Light, pixelEdge);
            Assert.NotEqual(Dark, pixelEdge);
        }
        else if (!host.Renderer.SupportsFilter(TextureFilter.Pixel))
        {
            Assert.Equal(nearestEdge, pixelEdge);
        }

        var name = halves == 3 ? "pixel-filter-x3" : "pixel-filter-x1.5";
        Golden.AssertMatches(host, renderer == "pixman" ? name : $"{name}-{renderer}");
    }
}
