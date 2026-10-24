using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

/// <summary>
/// Creates the backdrop blur that matches a renderer.
/// </summary>
public static class BackdropBlurs
{
    /// <summary>
    /// Creates a blur for the device of a renderer. The caller owns the result and registers it as <see cref="Basin.Capabilities.IBackgroundEffects"/>.
    /// </summary>
    /// <param name="renderer">The renderer that the blur will draw with.</param>
    /// <returns>A Vulkan or GL blur, or <see langword="null"/> when the renderer has no backdrop effects or no supported device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
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
