using Basin.Capabilities;

namespace Basin.UI.Skia;

/// <summary>
/// Picks the Skia chrome host that matches a renderer.
/// </summary>
public static class SkiaUIHosts
{
    /// <summary>
    /// Makes a Skia host on the GPU device of a GL, Vulkan or Skia renderer, and a CPU <see cref="SkiaUIHost"/> for every other renderer. A Skia renderer shares its own context with the host.
    /// </summary>
    /// <param name="renderer">The renderer that composites the chrome.</param>
    /// <returns>The host. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public static IUIHost For(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer switch
        {
            Basin.Render.Skia.SkiaGlRenderer skiaGl =>
                new SkiaGlUIHost(skiaGl.Device, skiaGl.Device.CreateAllocator(), skiaGl.Context),
            Basin.Render.Gl.GlRenderer gl =>
                new SkiaGlUIHost(gl.Device, gl.Device.CreateAllocator()),
            Basin.Render.Skia.SkiaVulkanRenderer skiaVk =>
                new SkiaVulkanUIHost(skiaVk.Device, skiaVk.Context, skiaVk.Device.CreateAllocator()),
            Basin.Render.Vulkan.VulkanRenderer vulkan =>
                new SkiaVulkanUIHost(vulkan.Device, null, vulkan.Device.CreateAllocator()),
            Basin.Render.Skia.SkiaGraphiteRenderer graphite =>
                new SkiaGraphiteUIHost(
                    graphite.Device, graphite.Context, graphite.Recorder, graphite.Device.CreateAllocator()),
            _ => new SkiaUIHost(),
        };
    }
}
