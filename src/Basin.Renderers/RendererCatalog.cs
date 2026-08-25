namespace Basin.Renderers;

public static class RendererCatalog
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "pixman",
        "gl",
        "vulkan",
        "skia",
        "skia-gl",
        "skia-vulkan",
        "skia-graphite",
        "impeller",
    ];

    public const string DefaultRenderNodePath = "/dev/dri/renderD128";

    public static string? FindRenderNode() =>
        File.Exists(DefaultRenderNodePath) ? DefaultRenderNodePath : null;

    public static bool NeedsGpu(string name) =>
        name is "gl" or "vulkan" or "skia-gl" or "skia-vulkan" or "skia-graphite" or "impeller";

    public static RenderStack Create(string name, string renderNodePath) => name switch
    {
        "pixman" => Basin.Render.Pixman.PixmanRenderer.CreateStack(),
        "skia" => Basin.Render.Skia.SkiaRenderer.CreateStack(),
        "gl" => Basin.Render.Gl.GlRenderer.CreateStack(renderNodePath),
        "vulkan" => Basin.Render.Vulkan.VulkanRenderer.CreateStack(renderNodePath),
        "skia-gl" => Basin.Render.Skia.SkiaGlRenderer.CreateStack(renderNodePath),
        "skia-vulkan" => Basin.Render.Skia.SkiaVulkanRenderer.CreateStack(renderNodePath),
        "skia-graphite" => Basin.Render.Skia.SkiaGraphiteRenderer.CreateStack(renderNodePath),
        "impeller" => Basin.Render.Impeller.ImpellerGlRenderer.CreateStack(renderNodePath),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a renderer basin ships"),
    };

    public static RenderStack CreateWithFallback(
        ref string name, string? renderNodePath, Action<RendererFallback>? report = null)
    {
        var path = renderNodePath ?? string.Empty;
        if (path.Length == 0 && NeedsGpu(name))
        {
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
                if (name == "pixman")
                {
                    throw;
                }

                var next = name == "gl" || path.Length == 0 ? "pixman" : "gl";
                report?.Invoke(new RendererFallback(name, next, error.Message));
                name = next;
            }
        }
    }
}
