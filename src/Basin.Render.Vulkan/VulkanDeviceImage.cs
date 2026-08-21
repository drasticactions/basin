using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanDeviceImage : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly ImportedDmabuf _imported;

    private VulkanDeviceImage(VulkanDevice device, ImportedDmabuf imported)
    {
        _device = device;
        _imported = imported;
    }

    public Image Image => _imported.Image;

    public ImageView View => _imported.View;

    public int Width => _imported.Width;

    public int Height => _imported.Height;

    public bool HasAlpha => _imported.HasAlpha;

    public static VulkanDeviceImage? TryImport(VulkanDevice device, in DmabufAttributes attributes, ImageUsageFlags usage)
    {
        try
        {
            var imported = VulkanDmabufImport.Import(device, attributes, usage);
            device.SubmitImmediate(commands => device.AcquireImported(commands, imported.Image));
            return new VulkanDeviceImage(device, imported);
        }
        catch (InvalidOperationException failure)
        {
            Basin.Diagnostics.BasinLog.Warn(
                $"dmabuf import rejected: {failure.Message} (format=0x{(uint)attributes.Format:X8} modifier=0x{attributes.Modifier:X} planes={attributes.PlaneCount} {attributes.Width}x{attributes.Height})");
            return null;
        }
    }

    public void RecordForeignAcquire(CommandBuffer commands) => _device.AcquireImported(commands, Image);

    public void RecordForeignRelease(CommandBuffer commands) => _device.ReleaseImported(commands, Image);

    public void Dispose()
    {
        _imported.Destroy(_device);
    }
}
