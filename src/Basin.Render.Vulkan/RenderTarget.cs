using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Pixman;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Vulkan;

internal sealed unsafe class RenderTarget : IVulkanRetired
{
    public ulong RetiredAt { get; set; }

    private VulkanRenderer? _retiringFor;

    public Image Image;
    public DeviceMemory Memory;
    public ImportedDmabuf? Imported;
    public DmabufAttributes Attributes;
    public ImageView View;
    public Framebuffer Framebuffer;
    public bool IsCpuReadback;
    public Silk.NET.Vulkan.Buffer Readback;
    public DeviceMemory ReadbackMemory;
    public void* ReadbackMapped;

    public bool TwoPassTarget;

    public bool CanSampleBackdrop;

    public ImageView SrgbView;

    public Image BlendImage;
    public DeviceMemory BlendMemory;
    public ImageView BlendView;
    public DescriptorAllocation BlendSet;

    public static RenderTarget Create(VulkanRenderer renderer, IBuffer buffer)
    {
        var vk = renderer.Dev.Api;
        var entry = new RenderTarget();
        BasinCounters.Track();
        VulkanFormatEntry formatEntry;

        if (buffer.TryGetDmabuf(out var attributes))
        {
            if (!renderer.Dev.FormatTable.TryGet(attributes.Format, out var formatProps) ||
                formatProps.Entry.Vk != Format.B8G8R8A8Unorm)
            {
                throw new InvalidOperationException($"{attributes.Format} is not renderable here; targets are B8G8R8A8 until per-format render setups exist");
            }

            formatEntry = formatProps.Entry;
            try
            {
                entry.Imported = VulkanDmabufImport.Import(
                    renderer.Dev, attributes, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit);
                entry.CanSampleBackdrop = true;
            }
            catch (InvalidOperationException)
            {
                entry.Imported = VulkanDmabufImport.Import(renderer.Dev, attributes, ImageUsageFlags.ColorAttachmentBit);
            }

            entry.Image = entry.Imported.Image;
            entry.View = entry.Imported.View;
            entry.Attributes = attributes;
            entry.TwoPassTarget = !(entry.Imported.UsingMutableSrgb && formatEntry.HasSrgb);
        }
        else
        {
            entry.IsCpuReadback = true;
            if (!buffer.BeginDataAccess(BufferDataAccess.Read, out var data))
            {
                throw new InvalidOperationException("target buffer is not CPU-readable");
            }

            DrmFormat cpuFormat;
            try
            {
                cpuFormat = data.Format;
            }
            finally
            {
                buffer.EndDataAccess();
            }

            if (!renderer.Dev.FormatTable.TryGet(cpuFormat, out var cpuProps) ||
                cpuProps.Entry.Vk != Format.B8G8R8A8Unorm)
            {
                throw new InvalidOperationException($"{cpuFormat} is not renderable here; targets are B8G8R8A8 until per-format render setups exist");
            }

            formatEntry = cpuProps.Entry;
            entry.TwoPassTarget = !formatEntry.HasSrgb;
            var viewFormats = stackalloc Format[2] { formatEntry.Vk, formatEntry.VkSrgb };
            var formatList = new ImageFormatListCreateInfo
            {
                SType = StructureType.ImageFormatListCreateInfo,
                ViewFormatCount = 2,
                PViewFormats = viewFormats,
            };
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                PNext = formatEntry.HasSrgb ? &formatList : null,
                Flags = formatEntry.HasSrgb ? ImageCreateFlags.CreateMutableFormatBit : 0,
                ImageType = ImageType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                Extent = new Extent3D((uint)buffer.Width, (uint)buffer.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            entry.CanSampleBackdrop = true;
            VulkanDevice.Check(vk.CreateImage(renderer.Dev.Device, in imageInfo, null, out entry.Image), "vkCreateImage(target cpu)");
            vk.GetImageMemoryRequirements(renderer.Dev.Device, entry.Image, out var requirements);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = renderer.Dev.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(renderer.Dev.Device, in allocateInfo, null, out entry.Memory), "vkAllocateMemory(target cpu)");
            VulkanDevice.Check(vk.BindImageMemory(renderer.Dev.Device, entry.Image, entry.Memory, 0), "vkBindImageMemory(target cpu)");

            var readbackInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)(buffer.Width * buffer.Height * 4),
                Usage = BufferUsageFlags.TransferDstBit,
            };
            VulkanDevice.Check(vk.CreateBuffer(renderer.Dev.Device, in readbackInfo, null, out entry.Readback), "vkCreateBuffer(readback)");
            vk.GetBufferMemoryRequirements(renderer.Dev.Device, entry.Readback, out var readbackRequirements);
            var readbackAllocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = readbackRequirements.Size,
                MemoryTypeIndex = renderer.Dev.ReadbackMemoryTypeFor(readbackRequirements.MemoryTypeBits),
            };
            VulkanDevice.Check(vk.AllocateMemory(renderer.Dev.Device, in readbackAllocate, null, out entry.ReadbackMemory), "vkAllocateMemory(readback)");
            VulkanDevice.Check(vk.BindBufferMemory(renderer.Dev.Device, entry.Readback, entry.ReadbackMemory, 0), "vkBindBufferMemory(readback)");
            void* mapped;
            VulkanDevice.Check(vk.MapMemory(renderer.Dev.Device, entry.ReadbackMemory, 0, readbackInfo.Size, 0, &mapped), "vkMapMemory(readback)");
            entry.ReadbackMapped = mapped;
        }

        if (entry.IsCpuReadback)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = entry.Image,
                ViewType = ImageViewType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(vk.CreateImageView(renderer.Dev.Device, in viewInfo, null, out entry.View), "vkCreateImageView(target)");
        }

        if (!entry.TwoPassTarget)
        {
            var srgbViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = entry.Image,
                ViewType = ImageViewType.Type2D,
                Format = formatEntry.VkSrgb,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(vk.CreateImageView(renderer.Dev.Device, in srgbViewInfo, null, out entry.SrgbView), "vkCreateImageView(target srgb)");

            var attachment = entry.SrgbView;
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderer.OnePass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = (uint)buffer.Width,
                Height = (uint)buffer.Height,
                Layers = 1,
            };
            VulkanDevice.Check(vk.CreateFramebuffer(renderer.Dev.Device, in framebufferInfo, null, out entry.Framebuffer), "vkCreateFramebuffer(one-pass)");
        }
        else
        {
            var blendInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R16G16B16A16Sfloat,
                Extent = new Extent3D((uint)buffer.Width, (uint)buffer.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.InputAttachmentBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            entry.CanSampleBackdrop = true;
            VulkanDevice.Check(vk.CreateImage(renderer.Dev.Device, in blendInfo, null, out entry.BlendImage), "vkCreateImage(blend)");
            vk.GetImageMemoryRequirements(renderer.Dev.Device, entry.BlendImage, out var blendRequirements);
            var blendAllocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = blendRequirements.Size,
                MemoryTypeIndex = renderer.Dev.MemoryTypeFor(blendRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(renderer.Dev.Device, in blendAllocate, null, out entry.BlendMemory), "vkAllocateMemory(blend)");
            VulkanDevice.Check(vk.BindImageMemory(renderer.Dev.Device, entry.BlendImage, entry.BlendMemory, 0), "vkBindImageMemory(blend)");

            var blendViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = entry.BlendImage,
                ViewType = ImageViewType.Type2D,
                Format = Format.R16G16B16A16Sfloat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(vk.CreateImageView(renderer.Dev.Device, in blendViewInfo, null, out entry.BlendView), "vkCreateImageView(blend)");
            entry.BlendSet = renderer.AllocateInputSet(entry.BlendView);

            var attachments = stackalloc ImageView[2] { entry.BlendView, entry.View };
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderer.TwoPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = (uint)buffer.Width,
                Height = (uint)buffer.Height,
                Layers = 1,
            };
            VulkanDevice.Check(vk.CreateFramebuffer(renderer.Dev.Device, in framebufferInfo, null, out entry.Framebuffer), "vkCreateFramebuffer(two-pass)");

            var blendImage = entry.BlendImage;
            renderer.Dev.SubmitImmediate(commands => renderer.Dev.TransitionToGeneral(commands, blendImage));
        }

        if (entry.IsCpuReadback)
        {
            var image = entry.Image;
            renderer.Dev.SubmitImmediate(commands => renderer.Dev.TransitionToGeneral(commands, image));
        }

        return entry;
    }

    public void Dispose(VulkanRenderer renderer)
    {
        BasinCounters.Untrack();
        _retiringFor = renderer;
        renderer.Dev.Ring.Retire(this);
    }

    void IVulkanRetired.ReleaseNow() => ReleaseNow(_retiringFor!);

    private void ReleaseNow(VulkanRenderer renderer)
    {
        var vk = renderer.Dev.Api;
        vk.DestroyFramebuffer(renderer.Dev.Device, Framebuffer, null);
        if (SrgbView.Handle != 0)
        {
            vk.DestroyImageView(renderer.Dev.Device, SrgbView, null);
        }

        if (BlendImage.Handle != 0)
        {
            renderer.InputDescriptors.Free(BlendSet);
            vk.DestroyImageView(renderer.Dev.Device, BlendView, null);
            vk.DestroyImage(renderer.Dev.Device, BlendImage, null);
            vk.FreeMemory(renderer.Dev.Device, BlendMemory, null);
        }

        if (Imported is { } imported)
        {
            imported.Destroy(renderer.Dev);
        }
        else
        {
            vk.DestroyImageView(renderer.Dev.Device, View, null);
            vk.DestroyImage(renderer.Dev.Device, Image, null);
            vk.FreeMemory(renderer.Dev.Device, Memory, null);
        }

        if (IsCpuReadback)
        {
            vk.UnmapMemory(renderer.Dev.Device, ReadbackMemory);
            vk.DestroyBuffer(renderer.Dev.Device, Readback, null);
            vk.FreeMemory(renderer.Dev.Device, ReadbackMemory, null);
        }
    }
}
