using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Owns a Vulkan device on a second GPU and copies its dmabufs into linear buffers that another GPU can import.
/// </summary>
public sealed unsafe class VulkanDeviceBlitter : IDisposable
{
    private readonly VulkanDevice _device;

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanDeviceBlitter"/> class. It opens a <see cref="VulkanDevice"/> on the node and throws when the node has no usable Vulkan device.
    /// </summary>
    /// <param name="renderNodePath">The path of the DRM render node of the source GPU.</param>
    public VulkanDeviceBlitter(string renderNodePath)
    {
        _device = new VulkanDevice(renderNodePath);
        DevicePath = renderNodePath;
    }

    /// <summary>
    /// Gets the path of the render node that the blitter opened.
    /// </summary>
    public string DevicePath { get; }

    /// <summary>
    /// Gets the RGB dmabuf formats and modifiers that <see cref="Convert"/> can read.
    /// </summary>
    public DrmFormatSet ImportableFormats => _device.SampleableRgbFormats;

    /// <summary>
    /// Starts a copy of a dmabuf into a linear buffer and does the first blit.
    /// </summary>
    /// <param name="source">The buffer from the GPU of this blitter.</param>
    /// <returns>The conversion, which the caller disposes, or <see langword="null"/> when the buffer is not a dmabuf in <see cref="ImportableFormats"/> or the import fails.</returns>
    public BlitConversion? Convert(IBuffer source)
    {
        if (!source.TryGetDmabuf(out var attributes) ||
            !ImportableFormats.Contains(attributes.Format, attributes.Modifier))
        {
            return null;
        }

        try
        {
            return new BlitConversion(_device, attributes);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Destroys the device. Dispose every conversion first.
    /// </summary>
    public void Dispose() => _device.Dispose();
}
