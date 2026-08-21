using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

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

    private SkiaGraphiteTarget(VulkanDevice device) => _device = device;

    public SKGraphiteBackendTexture BackendTexture { get; private set; } = null!;

    public SKSurface Surface { get; private set; } = null!;

    public SKCanvas Canvas { get; private set; } = null!;

    public bool IsCpuReadback { get; private set; }

    internal Image Image => _dmabuf?.Image ?? _cpuImage;

    public VulkanDeviceImage? Imported => _dmabuf;

    public static SkiaGraphiteTarget Create(VulkanDevice device, SKGraphiteRecorder recorder, IBuffer buffer)
    {
        var vk = device.Api;
        var target = new SkiaGraphiteTarget(device);
        var usage = SkiaVulkanRenderer.VkUsageRenderTarget | VkUsageInputAttachment;
        int tiling;

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

    public void Dispose()
    {
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
