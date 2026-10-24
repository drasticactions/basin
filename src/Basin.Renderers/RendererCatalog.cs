namespace Basin.Renderers;

/// <summary>
/// Creates any renderer that basin ships by name, with a fallback to a renderer that works on the machine.
/// </summary>
public static class RendererCatalog
{
    /// <summary>
    /// Gets the names that <see cref="Create"/> accepts, in a fixed order.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "pixman",
        "gl",
        "vulkan",
        "skia",
        "skia-gl",
        "skia-vulkan",
        "skia-graphite",
    ];

    /// <summary>
    /// The path of the first DRM render node.
    /// </summary>
    public const string DefaultRenderNodePath = "/dev/dri/renderD128";

    /// <summary>
    /// Finds a render node to use for a GPU renderer.
    /// </summary>
    /// <returns><see cref="DefaultRenderNodePath"/> when it exists, otherwise <see langword="null"/>.</returns>
    public static string? FindRenderNode() =>
        File.Exists(DefaultRenderNodePath) ? DefaultRenderNodePath : null;

    /// <summary>
    /// Tells whether a renderer needs a DRM render node.
    /// </summary>
    /// <param name="name">A name from <see cref="Names"/>.</param>
    /// <returns><see langword="true"/> for the GL, Vulkan and Skia GPU renderers.</returns>
    public static bool NeedsGpu(string name) =>
        name is "gl" or "vulkan" or "skia-gl" or "skia-vulkan" or "skia-graphite";

    /// <summary>
    /// Creates a renderer and its device allocator. It throws <see cref="ArgumentOutOfRangeException"/> for an unknown name, and the error of the renderer when it cannot start.
    /// </summary>
    /// <param name="name">A name from <see cref="Names"/>.</param>
    /// <param name="renderNodePath">The render node for a GPU renderer. A CPU renderer ignores it.</param>
    /// <returns>The renderer and its allocator, which the caller disposes.</returns>
    public static RenderStack Create(string name, string renderNodePath) => name switch
    {
        "pixman" => Basin.Render.Pixman.PixmanRenderer.CreateStack(),
        "skia" => Basin.Render.Skia.SkiaRenderer.CreateStack(),
        "gl" => Basin.Render.Gl.GlRenderer.CreateStack(renderNodePath),
        "vulkan" => Basin.Render.Vulkan.VulkanRenderer.CreateStack(renderNodePath),
        "skia-gl" => Basin.Render.Skia.SkiaGlRenderer.CreateStack(renderNodePath),
        "skia-vulkan" => Basin.Render.Skia.SkiaVulkanRenderer.CreateStack(renderNodePath),
        "skia-graphite" => Basin.Render.Skia.SkiaGraphiteRenderer.CreateStack(renderNodePath),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a renderer basin ships"),
    };

    /// <summary>
    /// Creates a renderer and steps down to GL and then to pixman when it cannot start. It reports each step through <paramref name="report"/>.
    /// </summary>
    /// <param name="name">The requested name. On return it holds the name of the renderer that started.</param>
    /// <param name="renderNodePath">The render node, or <see langword="null"/> when there is none. A GPU name then goes straight to pixman when the floor allows it.</param>
    /// <param name="report">The callback that receives each fallback, or <see langword="null"/>.</param>
    /// <param name="floor">
    /// The last renderer to try. When it fails, or the next step goes below it, the error propagates.
    /// A floor above pixman with no render node throws <see cref="InvalidOperationException"/> and reports nothing.
    /// </param>
    /// <returns>The renderer and its allocator, which the caller disposes.</returns>
    public static RenderStack CreateWithFallback(
        ref string name, string? renderNodePath, Action<RendererFallback>? report = null, string floor = "pixman")
    {
        var path = renderNodePath ?? string.Empty;
        if (path.Length == 0 && NeedsGpu(name))
        {
            if (floor != "pixman")
            {
                throw new InvalidOperationException($"no render node, and the floor is {floor}");
            }

            report?.Invoke(new RendererFallback(name, "pixman", null));
            name = "pixman";
        }

        while (true)
        {
            try
            {
                return Create(name, path);
            }
            catch (Exception error)
            {
                if (name == "pixman" || name == floor)
                {
                    throw;
                }

                var next = name == "gl" || path.Length == 0 ? "pixman" : "gl";
                if (next == "pixman" && floor != "pixman")
                {
                    throw;
                }

                report?.Invoke(new RendererFallback(name, next, error.Message));
                name = next;
            }
        }
    }
}
