using Basin.Capabilities;

namespace Basin.UI.Skia;

public static class SkiaUIHosts
{
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
