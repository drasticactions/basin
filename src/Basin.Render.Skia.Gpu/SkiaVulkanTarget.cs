using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

public sealed unsafe class SkiaVulkanTarget : IDisposable
{
    private readonly VulkanDevice _device;
    private VulkanDeviceImage? _dmabuf;
    private Image _cpuImage;
    private DeviceMemory _cpuMemory;

    private SkiaVulkanTarget(VulkanDevice device) => _device = device;

    public GRBackendRenderTarget BackendTarget { get; private set; } = null!;

    public SKSurface Surface { get; private set; } = null!;

    public SKCanvas Canvas { get; private set; } = null!;

    public bool IsCpuReadback { get; private set; }

    public Image Image => _dmabuf?.Image ?? _cpuImage;

    public VulkanDeviceImage? Imported => _dmabuf;

    public static SkiaVulkanTarget Create(VulkanDevice device, GRContext context, IBuffer buffer)
    {
        var vk = device.Api;
        var target = new SkiaVulkanTarget(device);
        uint tiling;

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

    public void Dispose()
    {
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

        _dmabuf?.Dispose();
        if (IsCpuReadback)
        {
            vk.DestroyImage(device.Device, _cpuImage, null);
            vk.FreeMemory(device.Device, _cpuMemory, null);
        }
    }
}
