using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// One render target of the Graphite renderer: an imported dmabuf image, or a device image that
/// reads back into a CPU buffer.
/// </summary>
public sealed unsafe class SkiaGraphiteTarget : IDisposable
{
    internal const uint VkUsageInputAttachment = 0x80;

    private readonly VulkanDevice _device;
    private VulkanDeviceImage? _dmabuf;
    private Image _cpuImage;
    private DeviceMemory _cpuMemory;
    private Silk.NET.Vulkan.Buffer _readback;
    private DeviceMemory _readbackMemory;
    private void* _readbackMapped;
    private ImageView _backdropView;

    private SkiaGraphiteTarget(VulkanDevice device) => _device = device;

    /// <summary>
    /// Gets the Graphite backend texture that wraps the image. The target owns it.
    /// </summary>
    public SKGraphiteBackendTexture BackendTexture { get; private set; } = null!;

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

    internal Image Image => _dmabuf?.Image ?? _cpuImage;

    /// <summary>
    /// Gets the imported dmabuf image, or <see langword="null"/> for a CPU readback target.
    /// </summary>
    public VulkanDeviceImage? Imported => _dmabuf;

    /// <summary>
    /// Makes a target for a buffer. A dmabuf must be ARGB8888 or XRGB8888, and a memory buffer must
    /// have 4 bytes per pixel. A failure throws.
    /// </summary>
    /// <param name="device">The Vulkan device that owns the image.</param>
    /// <param name="recorder">The Graphite recorder that draws into the target.</param>
    /// <param name="buffer">The buffer to draw into.</param>
    /// <returns>The new target, which the caller owns and must dispose.</returns>
    public static SkiaGraphiteTarget Create(VulkanDevice device, SKGraphiteRecorder recorder, IBuffer buffer)
    {
        var vk = device.Api;
        var target = new SkiaGraphiteTarget(device);
        var usage = SkiaVulkanRenderer.VkUsageRenderTarget | VkUsageInputAttachment;
        int tiling;

        if (buffer.TryGetDmabuf(out var dmabufAttributes) &&
            dmabufAttributes.Format is not (DrmFormat.Argb8888 or DrmFormat.Xrgb8888))
        {
            throw new NotSupportedException($"{dmabufAttributes.Format} is not a render target format for the Skia Graphite renderer.");
        }

        if (buffer.TryGetDmabuf(out var attributes) &&
            VulkanDeviceImage.TryImport(
                device, attributes,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.InputAttachmentBit |
                ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit) is { } imported)
        {
            target._dmabuf = imported;

            tiling = (int)SkiaVulkanRenderer.VkImageTilingOptimal;
        }
        else if (buffer.BeginDataAccess(BufferDataAccess.Read, out _))
        {
            buffer.EndDataAccess();
            if (buffer.Format.BytesPerPixel() != 4)
            {
                throw new NotSupportedException($"{buffer.Format} is not a CPU render target format for the Skia Graphite renderer.");
            }

            target.IsCpuReadback = true;
            tiling = (int)SkiaVulkanRenderer.VkImageTilingOptimal;
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
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.InputAttachmentBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out target._cpuImage), "vkCreateImage(graphite target)");
            vk.GetImageMemoryRequirements(device.Device, target._cpuImage, out var requirements);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out target._cpuMemory), "vkAllocateMemory(graphite target)");
            VulkanDevice.Check(vk.BindImageMemory(device.Device, target._cpuImage, target._cpuMemory, 0), "vkBindImageMemory(graphite target)");

            var readbackInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)(buffer.Width * buffer.Height * 4),
                Usage = BufferUsageFlags.TransferDstBit,
            };
            VulkanDevice.Check(vk.CreateBuffer(device.Device, in readbackInfo, null, out target._readback), "vkCreateBuffer(graphite readback)");
            vk.GetBufferMemoryRequirements(device.Device, target._readback, out var readbackRequirements);
            var readbackAllocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = readbackRequirements.Size,
                MemoryTypeIndex = device.ReadbackMemoryTypeFor(readbackRequirements.MemoryTypeBits),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in readbackAllocate, null, out target._readbackMemory), "vkAllocateMemory(graphite readback)");
            VulkanDevice.Check(vk.BindBufferMemory(device.Device, target._readback, target._readbackMemory, 0), "vkBindBufferMemory(graphite readback)");
            void* mapped;
            VulkanDevice.Check(vk.MapMemory(device.Device, target._readbackMemory, 0, readbackInfo.Size, 0, &mapped), "vkMapMemory(graphite readback)");
            target._readbackMapped = mapped;

            var image = target._cpuImage;
            device.SubmitImmediate(commands => device.TransitionToGeneral(commands, image));
        }
        else
        {
            throw new InvalidOperationException("render target is neither importable nor CPU-accessible");
        }

        target.BackendTexture = SkiaCensus.Track(SKGraphiteBackendTexture.CreateVulkan(
            buffer.Width, buffer.Height,
            new SKGraphiteVkTextureInfo
            {
                SampleCount = 1,
                Format = (int)SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
                ImageTiling = tiling,
                ImageUsageFlags = usage,
                SharingMode = 0,
                AspectMask = 1,
            },
            (int)SkiaVulkanRenderer.VkImageLayoutGeneral,
            device.QueueFamily,
            (nint)target.Image.Handle));
        var surface = SKSurface.Create(recorder, target.BackendTexture, SKColorType.Bgra8888);
        if (surface is null)
        {
            target.Dispose();
            throw new InvalidOperationException("Graphite rejected the render target's image.");
        }

        target.Surface = SkiaCensus.Track(surface);
        target.Canvas = surface.Canvas;
        return target;
    }

    internal ImageView BackdropView()
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
        VulkanDevice.Check(_device.Api.CreateImageView(_device.Device, in viewInfo, null, out _backdropView), "vkCreateImageView(graphite backdrop)");
        return _backdropView;
    }

    /// <summary>
    /// Copies the target's pixels into the buffer's memory through a mapped readback buffer. It submits
    /// and waits on the device first. The copy is skipped when the buffer refuses write access.
    /// </summary>
    /// <param name="buffer">The memory buffer that the target was created for.</param>
    public void ReadInto(IBuffer buffer)
    {
        _device.SubmitImmediate(
            (Target: this, Width: (uint)buffer.Width, Height: (uint)buffer.Height),
            static (state, commands) => state.Target.RecordReadback(commands, state.Width, state.Height));

        if (!buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return;
        }

        try
        {
            var rowBytes = buffer.Width * 4;
            for (var y = 0; y < buffer.Height; y++)
            {
                System.Buffer.MemoryCopy(
                    (byte*)_readbackMapped + y * rowBytes,
                    (void*)(view.Data + y * view.Stride),
                    rowBytes,
                    rowBytes);
            }
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private void RecordReadback(CommandBuffer commands, uint width, uint height)
    {
        var vk = _device.Api;
        var rendered = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _cpuImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, &rendered);

        var copy = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(width, height, 1),
        };
        vk.CmdCopyImageToBuffer(commands, _cpuImage, ImageLayout.General, _readback, 1, &copy);

        var toHost = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit,
            0, 1, &toHost, 0, null, 0, null);
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

        if (BackendTexture is not null)
        {
            SkiaCensus.Release(BackendTexture);
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
            vk.UnmapMemory(device.Device, _readbackMemory);
            vk.DestroyBuffer(device.Device, _readback, null);
            vk.FreeMemory(device.Device, _readbackMemory, null);
            vk.DestroyImage(device.Device, _cpuImage, null);
            vk.FreeMemory(device.Device, _cpuMemory, null);
        }
    }
}
