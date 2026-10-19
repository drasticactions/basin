using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

public static class BackdropGlass
{
    public static IBackdropGlass? For(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!renderer.SupportsBackdropEffects)
        {
            return null;
        }

        return renderer.Device switch
        {
            VulkanDevice vulkan => new VulkanBackdropGlass(vulkan),
            GlDevice gl => new GlBackdropGlass(gl),
            _ => null,
        };
    }
}
