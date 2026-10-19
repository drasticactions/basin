namespace Basin.Sdl;

public static class SdlRenderDevices
{
    public const string Raster = "skia";

    public const string Gl = "skia-gl";

    public const string Vulkan = "skia-vulkan";

    public const string Metal = "skia-metal";

    public static IReadOnlyList<string> Names { get; } = OperatingSystem.IsMacOS() ? [Raster, Gl, Metal] : [Raster, Gl, Vulkan];

    public static string DefaultName => OperatingSystem.IsMacOS() ? Metal : Gl;

    public static ISdlRenderDevice Create(string name) => name switch
    {
        Raster => new SdlRasterDevice(),
        Gl => new SdlGlDevice(),
        Vulkan when !OperatingSystem.IsMacOS() => new SdlVulkanDevice(),
        Metal when OperatingSystem.IsMacOS() => new SdlMetalDevice(),
        _ => throw new ArgumentException($"unknown SDL render lease '{name}' (expected {string.Join(", ", Names)})", nameof(name)),
    };
}
