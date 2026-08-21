using Basin.Diagnostics;
using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanColorLut : IColorLut
{
    private readonly VulkanRenderer _renderer;
    private readonly Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _view;
    private readonly DescriptorAllocation _allocation;

    internal DescriptorSet Set => _allocation.Set;

    internal VulkanColorLut(VulkanRenderer renderer, ColorLut3D lut)
    {
        _renderer = renderer;
        var vk = renderer.Dev.Api;
        var size = (uint)lut.Size;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type3D,
            Format = Format.R16G16B16A16Sfloat,
            Extent = new Extent3D(size, size, size),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(renderer.Dev.Device, in imageInfo, null, out _image), "vkCreateImage(lut)");
        vk.GetImageMemoryRequirements(renderer.Dev.Device, _image, out var requirements);
        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = renderer.Dev.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(renderer.Dev.Device, in allocateInfo, null, out _memory), "vkAllocateMemory(lut)");
        VulkanDevice.Check(vk.BindImageMemory(renderer.Dev.Device, _image, _memory, 0), "vkBindImageMemory(lut)");

        var texels = lut.Size * lut.Size * lut.Size;
        var stagingInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)texels * 8,
            Usage = BufferUsageFlags.TransferSrcBit,
        };
        VulkanDevice.Check(vk.CreateBuffer(renderer.Dev.Device, in stagingInfo, null, out var staging), "vkCreateBuffer(lut staging)");
        vk.GetBufferMemoryRequirements(renderer.Dev.Device, staging, out var stagingRequirements);
        var stagingAllocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = stagingRequirements.Size,
            MemoryTypeIndex = renderer.Dev.MemoryTypeFor(
                stagingRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(renderer.Dev.Device, in stagingAllocate, null, out var stagingMemory), "vkAllocateMemory(lut staging)");
        VulkanDevice.Check(vk.BindBufferMemory(renderer.Dev.Device, staging, stagingMemory, 0), "vkBindBufferMemory(lut staging)");
        void* mapped;
        VulkanDevice.Check(vk.MapMemory(renderer.Dev.Device, stagingMemory, 0, stagingInfo.Size, 0, &mapped), "vkMapMemory(lut staging)");
        var halves = (ushort*)mapped;
        var one = BitConverter.HalfToUInt16Bits((Half)1f);
        for (var i = 0; i < texels; i++)
        {
            halves[i * 4 + 0] = BitConverter.HalfToUInt16Bits((Half)lut.Data[i * 3 + 0]);
            halves[i * 4 + 1] = BitConverter.HalfToUInt16Bits((Half)lut.Data[i * 3 + 1]);
            halves[i * 4 + 2] = BitConverter.HalfToUInt16Bits((Half)lut.Data[i * 3 + 2]);
            halves[i * 4 + 3] = one;
        }

        vk.UnmapMemory(renderer.Dev.Device, stagingMemory);

        var image = _image;
        renderer.Dev.SubmitImmediate(commands =>
        {
            renderer.Dev.TransitionToGeneral(commands, image);
            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D(size, size, size),
            };
            vk.CmdCopyBufferToImage(commands, staging, image, ImageLayout.General, 1, &copy);
            var visible = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
            };
            vk.CmdPipelineBarrier(
                commands,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.FragmentShaderBit,
                0, 1, &visible, 0, null, 0, null);
        });

        vk.DestroyBuffer(renderer.Dev.Device, staging, null);
        vk.FreeMemory(renderer.Dev.Device, stagingMemory, null);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type3D,
            Format = Format.R16G16B16A16Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(renderer.Dev.Device, in viewInfo, null, out _view), "vkCreateImageView(lut)");
        _allocation = renderer.AllocateLutSet(_view);
        BasinCounters.Track();
    }

    public void Dispose()
    {
        var vk = _renderer.Dev.Api;
        _renderer.Descriptors.Free(_allocation);
        vk.DestroyImageView(_renderer.Dev.Device, _view, null);
        vk.DestroyImage(_renderer.Dev.Device, _image, null);
        vk.FreeMemory(_renderer.Dev.Device, _memory, null);
        BasinCounters.Untrack();
    }
}
