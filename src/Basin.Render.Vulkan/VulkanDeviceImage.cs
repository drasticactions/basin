using Silk.NET.Vulkan;
using static Basin.Render.Vulkan.VulkanLog;

namespace Basin.Render.Vulkan;

/// <summary>
/// Owns a Vulkan image that is imported from a dmabuf, for code that records its own commands on a <see cref="VulkanDevice"/>.
/// </summary>
public sealed unsafe class VulkanDeviceImage : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly ImportedDmabuf _imported;

    private VulkanDeviceImage(VulkanDevice device, ImportedDmabuf imported)
    {
        _device = device;
        _imported = imported;
    }

    /// <summary>
    /// Gets the imported image. It is in the general layout and owned by this device after the import.
    /// </summary>
    public Image Image => _imported.Image;

    /// <summary>
    /// Gets a view of the whole image.
    /// </summary>
    public ImageView View => _imported.View;

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public int Width => _imported.Width;

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public int Height => _imported.Height;

    /// <summary>
    /// Gets a value indicating whether the format of the dmabuf has an alpha channel.
    /// </summary>
    public bool HasAlpha => _imported.HasAlpha;

    /// <summary>
    /// Imports a dmabuf as an image and acquires it from the foreign queue family. It blocks until the acquire finishes.
    /// </summary>
    /// <param name="device">The device to import on.</param>
    /// <param name="attributes">The dmabuf to import. Its fds stay owned by the caller.</param>
    /// <param name="usage">The usages that the image must support.</param>
    /// <returns>The image, which the caller disposes, or <see langword="null"/> when the device rejects the dmabuf.</returns>
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
            Log.Warn(
                $"dmabuf import rejected: {failure.Message} (format=0x{(uint)attributes.Format:X8} modifier=0x{attributes.Modifier:X} planes={attributes.PlaneCount} {attributes.Width}x{attributes.Height})");
            return null;
        }
    }

    /// <summary>
    /// Records a barrier that takes the image from the foreign queue family again, after another device wrote it.
    /// </summary>
    /// <param name="commands">The command buffer to record into.</param>
    public void RecordForeignAcquire(CommandBuffer commands) => _device.AcquireImported(commands, Image);

    /// <summary>
    /// Records a barrier that gives the image to the foreign queue family, before another device reads it.
    /// </summary>
    /// <param name="commands">The command buffer to record into.</param>
    public void RecordForeignRelease(CommandBuffer commands) => _device.ReleaseImported(commands, Image);

    /// <summary>
    /// Destroys the view, the image and its imported memory. The GPU must be done with the image.
    /// </summary>
    public void Dispose()
    {
        _imported.Destroy(_device);
    }
}
