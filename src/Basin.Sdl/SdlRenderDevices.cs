namespace Basin.Sdl;

/// <summary>
/// Names and creates the SDL render devices. The names are the <c>--renderer</c> values of an SDL host.
/// </summary>
public static class SdlRenderDevices
{
    /// <summary>
    /// The name of the raster device, which copies a CPU surface into an SDL texture.
    /// </summary>
    public const string Raster = "skia";

    /// <summary>
    /// The name of the GL device. On Linux it forces EGL, so the host can import dmabufs.
    /// </summary>
    public const string Gl = "skia-gl";

    /// <summary>
    /// The name of the Vulkan device. It is not offered on macOS.
    /// </summary>
    public const string Vulkan = "skia-vulkan";

    /// <summary>
    /// The name of the Metal device. It is offered on macOS only.
    /// </summary>
    public const string Metal = "skia-metal";

    /// <summary>
    /// Gets the device names that <see cref="Create"/> accepts on this operating system.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = OperatingSystem.IsMacOS() ? [Raster, Gl, Metal] : [Raster, Gl, Vulkan];

    /// <summary>
    /// Gets the device name to use when the consumer names none: <see cref="Metal"/> on macOS, else <see cref="Gl"/>.
    /// </summary>
    public static string DefaultName => OperatingSystem.IsMacOS() ? Metal : Gl;

    /// <summary>
    /// Creates the device for a name. The device creates its context with its first lease.
    /// </summary>
    /// <param name="name">One of <see cref="Names"/>.</param>
    /// <returns>A new device that the caller owns.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not in <see cref="Names"/>.</exception>
    public static ISdlRenderDevice Create(string name) => name switch
    {
        Raster => new SdlRasterDevice(),
        Gl => new SdlGlDevice(),
        Vulkan when !OperatingSystem.IsMacOS() => new SdlVulkanDevice(),
        Metal when OperatingSystem.IsMacOS() => new SdlMetalDevice(),
        _ => throw new ArgumentException($"The SDL render lease '{name}' is unknown. Use one of {string.Join(", ", Names)}.", nameof(name)),
    };
}
