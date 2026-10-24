using Basin.Capabilities;
using Basin.Render.Gl;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Basin.UI.ImGui;

namespace Basin.Renderers;

/// <summary>
/// Creates the Dear ImGui UI host that fits a renderer.
/// </summary>
public static class ImGuiHosts
{
    /// <summary>
    /// Creates an ImGui UI host. It tries a Vulkan host on the device of a Vulkan renderer first. It then makes a GL host that shares the device of a GL renderer or opens its own.
    /// </summary>
    /// <param name="renderer">The renderer that composites the UI surfaces.</param>
    /// <param name="allocator">The allocator for the surface buffers, or <see langword="null"/>. A GL host on its own device ignores it.</param>
    /// <param name="renderNodePath">The render node to use when the renderer has no device, or <see langword="null"/>.</param>
    /// <param name="declined">The reason when the GL host was not made, otherwise <see langword="null"/>.</param>
    /// <param name="vulkanDeclined">The reason when the Vulkan host was tried and not made, otherwise <see langword="null"/>.</param>
    /// <param name="options">The options of the host, or <see langword="null"/> for the defaults.</param>
    /// <returns>The host, which the caller disposes, or <see langword="null"/> when no host started.</returns>
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
