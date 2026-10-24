using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// One render target of the Ganesh Vulkan renderer: an imported dmabuf image, or a device image
/// that reads back into a CPU buffer.
/// </summary>
public sealed unsafe class SkiaVulkanTarget : IDisposable
{
    private readonly VulkanDevice _device;
    private VulkanDeviceImage? _dmabuf;
    private Image _cpuImage;
    private DeviceMemory _cpuMemory;
    private ImageView _backdropView;

    private SkiaVulkanTarget(VulkanDevice device) => _device = device;

    /// <summary>
    /// Gets the Ganesh render target that wraps the image. The target owns it.
    /// </summary>
    public GRBackendRenderTarget BackendTarget { get; private set; } = null!;

    /// <summary>
    /// Gets the Skia surface that draws into the target image. The target owns it.
    /// </summary>
    public SKSurface Surface { get; private set; } = null!;

    /// <summary>
    /// Gets the canvas of <see cref="Surface"/>.
    /// </summary>
    public SKCanvas Canvas { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the target draws into a device image that <see cref="ReadInto"/>
    /// copies into a memory buffer.
    /// </summary>
    public bool IsCpuReadback { get; private set; }

    /// <summary>
    /// Gets or sets the buffer write count that the target last matched. A CPU target whose buffer has
    /// a newer write count is out of date and must be made again.
    /// </summary>
    public ulong SeenWrites { get; set; }

    /// <summary>
    /// Gets the Vulkan image that the target draws into.
    /// </summary>
    public Image Image => _dmabuf?.Image ?? _cpuImage;

    /// <summary>
    /// Gets the imported dmabuf image, or <see langword="null"/> for a CPU readback target.
    /// </summary>
    public VulkanDeviceImage? Imported => _dmabuf;

    /// <summary>
    /// Makes a target for a buffer. A dmabuf must be ARGB8888 or XRGB8888, and a memory buffer must
    /// have 4 bytes per pixel. A failure throws.
    /// </summary>
    /// <param name="device">The Vulkan device that owns the image.</param>
    /// <param name="context">The Ganesh context that draws into the target.</param>
    /// <param name="buffer">The buffer to draw into.</param>
    /// <returns>The new target, which the caller owns and must dispose.</returns>
    public static SkiaVulkanTarget Create(VulkanDevice device, GRContext context, IBuffer buffer)
    {
        var vk = device.Api;
        var target = new SkiaVulkanTarget(device);
        uint tiling;

        if (buffer.TryGetDmabuf(out var dmabufAttributes) &&
            dmabufAttributes.Format is not (DrmFormat.Argb8888 or DrmFormat.Xrgb8888))
        {
            throw new NotSupportedException($"{dmabufAttributes.Format} is not a render target format for the Skia Vulkan renderer.");
        }

        if (buffer.TryGetDmabuf(out var attributes) &&
            VulkanDeviceImage.TryImport(
                device, attributes,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit) is { } imported)
        {
            target._dmabuf = imported;
            tiling = SkiaVulkanRenderer.VkImageTilingDrmFormatModifier;
        }
        else if (buffer.BeginDataAccess(BufferDataAccess.Read, out _))
        {
            buffer.EndDataAccess();
            if (buffer.Format.BytesPerPixel() != 4)
            {
                throw new NotSupportedException($"{buffer.Format} is not a CPU render target format for the Skia Vulkan renderer.");
            }

            target.IsCpuReadback = true;
            tiling = SkiaVulkanRenderer.VkImageTilingOptimal;
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                Extent = new Extent3D((uint)buffer.Width, (uint)buffer.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out target._cpuImage), "vkCreateImage(skia target)");
            vk.GetImageMemoryRequirements(device.Device, target._cpuImage, out var requirements);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out target._cpuMemory), "vkAllocateMemory(skia target)");
            VulkanDevice.Check(vk.BindImageMemory(device.Device, target._cpuImage, target._cpuMemory, 0), "vkBindImageMemory(skia target)");

            var image = target._cpuImage;
            device.SubmitImmediate(commands => device.TransitionToGeneral(commands, image));
        }
        else
        {
            throw new InvalidOperationException("render target is neither importable nor CPU-accessible");
        }

        target.BackendTarget = SkiaCensus.Track(new GRBackendRenderTarget(buffer.Width, buffer.Height, new GRVkImageInfo
        {
            Image = (ulong)target.Image.Handle,
            ImageTiling = tiling,
            ImageLayout = SkiaVulkanRenderer.VkImageLayoutGeneral,
            Format = SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
            ImageUsageFlags = SkiaVulkanRenderer.VkUsageRenderTarget,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = device.QueueFamily,
        }));
        var surface = SKSurface.Create(context, target.BackendTarget, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888);
        if (surface is null)
        {
            target.Dispose();
            throw new InvalidOperationException("Ganesh rejected the render target's image.");
        }

        target.Surface = SkiaCensus.Track(surface);
        target.Canvas = surface.Canvas;
        return target;
    }

    /// <summary>
    /// Gets an image view of the target for a backdrop effect. The target makes the view on the first
    /// call and owns it.
    /// </summary>
    /// <returns>The view, which stays valid until the target is disposed.</returns>
    public ImageView BackdropView()
    {
        if (_backdropView.Handle != 0)
        {
            return _backdropView;
        }

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = ImageViewType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(_device.Api.CreateImageView(_device.Device, in viewInfo, null, out _backdropView), "vkCreateImageView(skia backdrop)");
        return _backdropView;
    }

    /// <summary>
    /// Copies the target's pixels into the buffer's memory. Nothing happens when the buffer refuses
    /// write access.
    /// </summary>
    /// <param name="buffer">The buffer that the target was created for.</param>
    public void ReadInto(IBuffer buffer)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return;
        }

        try
        {
            var info = new SKImageInfo(buffer.Width, buffer.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _ = Surface.ReadPixels(info, view.Data, view.Stride, 0, 0);
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private bool _disposed;

    /// <summary>
    /// Waits for the device to idle, then frees the surface, the image and its memory. A second call
    /// does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var device = _device;
        var vk = device.Api;
        _ = vk.DeviceWaitIdle(device.Device);
        if (Surface is not null)
        {
            SkiaCensus.Release(Surface);
        }

        if (BackendTarget is not null)
        {
            SkiaCensus.Release(BackendTarget);
        }

        if (_backdropView.Handle != 0)
        {
            device.NotifyImageViewDestroying(_backdropView);
            vk.DestroyImageView(device.Device, _backdropView, null);
            _backdropView = default;
        }

        _dmabuf?.Dispose();
        if (IsCpuReadback)
        {
            vk.DestroyImage(device.Device, _cpuImage, null);
            vk.FreeMemory(device.Device, _cpuMemory, null);
        }
    }
}
