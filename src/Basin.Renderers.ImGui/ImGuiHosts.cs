using Basin.Capabilities;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Basin.UI.ImGui;

namespace Basin.Renderers;

public static class ImGuiHosts
{
    public static ImGuiUIHost? For(
        IRenderer renderer,
        IAllocator? allocator,
        string? renderNodePath,
        out string? declined,
        out string? vulkanDeclined,
        ImGuiHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        vulkanDeclined = null;
        if (renderer.Device is VulkanDevice vulkan)
        {
            var host = ImGuiVulkanUIHost.TryCreate(
                vulkan, vulkan.DevicePath, renderer.DmabufTextureFormats, out vulkanDeclined, allocator, options);
            if (host is not null)
            {
                declined = null;
                return host;
            }
        }

        var shared = renderer switch
        {
            GlRenderer gl => gl.Device,
            SkiaGlRenderer skia => skia.Device,
            _ => null,
        };
        return ImGuiGlUIHost.TryCreate(
            shared,
            renderer.Device?.DevicePath ?? renderNodePath,
            renderer.DmabufTextureFormats,
            out declined,
            shared is null ? null : allocator,
            options);
    }
}
