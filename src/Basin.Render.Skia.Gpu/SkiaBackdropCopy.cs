using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.Render.Skia;

internal sealed unsafe class SkiaBackdropCopy
{
    private readonly VulkanDevice _device;
    private DeviceMemory _memory;

    public SkiaBackdropCopy(VulkanDevice device) => _device = device;

    public Image Image { get; private set; }

    public Extent2D Extent { get; private set; }

    public bool Matches(Extent2D extent) =>
        Image.Handle != 0 && Extent.Width == extent.Width && Extent.Height == extent.Height;

    public void Create(Extent2D extent)
    {
        Release();
        var vk = _device.Api;
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R16G16B16A16Sfloat,
            Extent = new Extent3D(extent.Width, extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out var image), "vkCreateImage(skia backdrop copy)");
        vk.GetImageMemoryRequirements(_device.Device, image, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(_device.Device, in allocate, null, out _memory), "vkAllocateMemory(skia backdrop copy)");
        VulkanDevice.Check(vk.BindImageMemory(_device.Device, image, _memory, 0), "vkBindImageMemory(skia backdrop copy)");
        Image = image;
        Extent = extent;
        _device.SubmitImmediate((vk, image), static (state, commands) =>
        {
            var barrier = Barrier(state.image, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal, 0, AccessFlags.ShaderReadBit);
            state.vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.FragmentShaderBit,
                0, 0, null, 0, null, 1, &barrier);
        });
    }

    public void Release()
    {
        if (Image.Handle == 0)
        {
            return;
        }

        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        vk.DestroyImage(_device.Device, Image, null);
        vk.FreeMemory(_device.Device, _memory, null);
        Image = default;
        _memory = default;
        Extent = default;
    }

    public bool Run(
        IVulkanBackdropEffect effect, Image target, ImageView targetView, ImageLayout targetLayout,
        Extent2D extent, in Box bounds, object? key, out Box source)
    {
        var vk = _device.Api;
        var commands = _device.BeginCommands();
        var toGeneral = Barrier(target, targetLayout, ImageLayout.General, AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit);
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &toGeneral);

        var context = new VulkanBackdropContext
        {
            Device = _device,
            Commands = commands,
            Backdrop = targetView,
            TargetExtent = extent,
            Bounds = bounds,
            Key = key,
        };
        var recorded = effect.Record(in context, out var result);
        var copied = recorded && result.Image.Handle != 0 && Image.Handle != 0
            && result.Source.X >= 0 && result.Source.Y >= 0
            && result.Source.Right <= Extent.Width && result.Source.Bottom <= Extent.Height;
        source = result.Source;

        var restored = Barrier(
            target, ImageLayout.General, targetLayout,
            AccessFlags.ShaderReadBit, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit);
        if (!copied)
        {
            vk.CmdPipelineBarrier(
                commands,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.ColorAttachmentOutputBit,
                0, 0, null, 0, null, 1, &restored);
            _device.Submit(commands);
            return false;
        }

        var written = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };
        var before = stackalloc ImageMemoryBarrier[2]
        {
            restored,
            Barrier(Image, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal, AccessFlags.ShaderReadBit, AccessFlags.TransferWriteBit),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.ColorAttachmentOutputBit,
            0, 1, &written, 0, null, 2, before);

        var subresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1);
        var region = new ImageCopy
        {
            SrcSubresource = subresource,
            SrcOffset = new Offset3D(result.Source.X, result.Source.Y, 0),
            DstSubresource = subresource,
            DstOffset = new Offset3D(result.Source.X, result.Source.Y, 0),
            Extent = new Extent3D((uint)result.Source.Width, (uint)result.Source.Height, 1),
        };
        vk.CmdCopyImage(commands, result.Image, ImageLayout.General, Image, ImageLayout.TransferDstOptimal, 1, &region);

        var toSample = Barrier(Image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &toSample);
        _device.Submit(commands);
        return true;
    }

    private static ImageMemoryBarrier Barrier(Image image, ImageLayout from, ImageLayout to, AccessFlags src, AccessFlags dst) => new()
    {
        SType = StructureType.ImageMemoryBarrier,
        SrcAccessMask = src,
        DstAccessMask = dst,
        OldLayout = from,
        NewLayout = to,
        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
        Image = image,
        SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
    };
}
