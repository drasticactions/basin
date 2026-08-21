using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanDeviceBlitter : IDisposable
{
    private readonly VulkanDevice _device;

    public VulkanDeviceBlitter(string renderNodePath)
    {
        _device = new VulkanDevice(renderNodePath);
        DevicePath = renderNodePath;
    }

    public string DevicePath { get; }

    public DrmFormatSet ImportableFormats => _device.SampleableRgbFormats;

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

    public void Dispose() => _device.Dispose();
}
