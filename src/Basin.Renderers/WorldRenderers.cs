using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Render3D.Gl;
using Basin.Render3D.Vulkan;
using Basin.Scene3D;

namespace Basin.Renderers;

/// <summary>
/// Creates the 3D world renderer that matches a 2D renderer, so that the 3D pass can open its textures.
/// </summary>
public static class WorldRenderers
{
    /// <summary>
    /// Creates a world renderer on the device of a GL or Vulkan renderer.
    /// </summary>
    /// <param name="renderer">The 2D renderer whose textures the 3D pass samples.</param>
    /// <param name="declined">The reason when no world renderer was made, otherwise <see langword="null"/>.</param>
    /// <returns>The world renderer, which the caller disposes, or <see langword="null"/> for any other renderer.</returns>
    public static IWorldRenderer? For(IRenderer renderer, out string? declined)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        declined = null;
        switch (renderer)
        {
            case VulkanRenderer vulkan:
                return new VulkanWorldRenderer(vulkan.Device);
            case GlRenderer gl:
                return new GlWorldRenderer(gl.Device);
            default:
                declined = "its textures are not GL or Vulkan textures, so the 3D pass cannot open them";
                return null;
        }
    }
}
