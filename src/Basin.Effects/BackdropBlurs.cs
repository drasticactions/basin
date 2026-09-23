using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

public static class BackdropBlurs
{
    public static IBackdropBlur? For(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (!renderer.SupportsBackdropEffects)
        {
            return null;
        }

        return renderer.Device switch
        {
            VulkanDevice vulkan => new VulkanBackdropBlur(vulkan),
            GlDevice gl => new GlBackdropBlur(gl),
            _ => null,
        };
    }
}
