using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

/// <summary>
/// Creates the glass backdrop effect that matches a renderer.
/// </summary>
public static class BackdropGlass
{
    /// <summary>
    /// Creates a glass effect for the device of a renderer. The caller owns the result.
    /// </summary>
    /// <param name="renderer">The renderer that the glass will draw with.</param>
    /// <returns>A Vulkan or GL glass effect, or <see langword="null"/> when the renderer has no backdrop effects or no supported device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
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
