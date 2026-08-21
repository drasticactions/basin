using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Xunit;

namespace Basin.Tests;

public sealed class YcbcrTests
{
    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int fd);

    [Fact]
    public unsafe void Nv12_imports_and_draws()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var renderer = new VulkanRenderer(CompositorTestHost.RenderNodePath);
        Assert.SkipWhen(
            !renderer.DmabufTextureFormats.Contains(DrmFormat.Nv12),
            "the driver probes no NV12 sampling");

        Assert.False(renderer.Device.SampleableRgbFormats.Contains(DrmFormat.Nv12));

        var device = renderer.Device;
        var vk = device.Api;
        var modifiers = renderer.DmabufTextureFormats.ModifiersOf(DrmFormat.Nv12).ToArray();

        var modifierList = new ImageDrmFormatModifierListCreateInfoEXT
        {
            SType = StructureType.ImageDrmFormatModifierListCreateInfoExt,
            DrmFormatModifierCount = (uint)modifiers.Length,
        };
        var external = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            PNext = &modifierList,
            HandleTypes = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
        };
        Image image;
        fixed (ulong* modifiersPtr = modifiers)
        {
            modifierList.PDrmFormatModifiers = modifiersPtr;
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                PNext = &external,
                ImageType = ImageType.Type2D,
                Format = Format.G8B8R82Plane420Unorm,
                Extent = new Extent3D(64, 64, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.DrmFormatModifierExt,
                Usage = ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out image), "vkCreateImage(nv12)");
        }

        vk.GetImageMemoryRequirements(device.Device, image, out var requirements);
        var dedicated = new MemoryDedicatedAllocateInfo
        {
            SType = StructureType.MemoryDedicatedAllocateInfo,
            Image = image,
        };
        var export = new ExportMemoryAllocateInfo
        {
            SType = StructureType.ExportMemoryAllocateInfo,
            PNext = &dedicated,
            HandleTypes = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
        };
        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = &export,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, 0),
        };
        VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out var memory), "vkAllocateMemory(nv12)");
        VulkanDevice.Check(vk.BindImageMemory(device.Device, image, memory, 0), "vkBindImageMemory(nv12)");

        Assert.True(vk.TryGetDeviceExtension(device.Instance, device.Device, out ExtImageDrmFormatModifier modifierExt));
        var modifierProps = new ImageDrmFormatModifierPropertiesEXT { SType = StructureType.ImageDrmFormatModifierPropertiesExt };
        VulkanDevice.Check(
            modifierExt.GetImageDrmFormatModifierProperties(device.Device, image, &modifierProps),
            "vkGetImageDrmFormatModifierPropertiesEXT");
        Assert.True(device.TryGetModifierPlaneCount(DrmFormat.Nv12, modifierProps.DrmFormatModifier, out var planeCount));

        var getFd = new MemoryGetFdInfoKHR
        {
            SType = StructureType.MemoryGetFDInfoKhr,
            Memory = memory,
            HandleType = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
        };
        int fd;
        VulkanDevice.Check(device.ExternalMemoryFd.GetMemoryF(device.Device, in getFd, &fd), "vkGetMemoryFdKHR(nv12)");

        var attributes = new DmabufAttributes
        {
            Width = 64,
            Height = 64,
            Format = DrmFormat.Nv12,
            Modifier = modifierProps.DrmFormatModifier,
            PlaneCount = (int)planeCount,
        };
        for (var plane = 0; plane < planeCount; plane++)
        {
            var aspect = plane switch
            {
                0 => ImageAspectFlags.MemoryPlane0BitExt,
                1 => ImageAspectFlags.MemoryPlane1BitExt,
                2 => ImageAspectFlags.MemoryPlane2BitExt,
                _ => ImageAspectFlags.MemoryPlane3BitExt,
            };
            var subresource = new ImageSubresource(aspect, 0, 0);
            vk.GetImageSubresourceLayout(device.Device, image, in subresource, out var layout);
            attributes.Fds[plane] = plane == 0 ? fd : dup(fd);
            attributes.Offsets[plane] = (uint)layout.Offset;
            attributes.Strides[plane] = (uint)layout.RowPitch;
        }

        var source = new DmabufBuffer(attributes);
        var texture = renderer.ImportTexture(source);
        Assert.NotNull(texture);

        var target = new MemoryBuffer(64, 64, DrmFormat.Xrgb8888);
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions { DstBox = new Box(0, 0, 64, 64) });
        Assert.True(pass.Submit());

        texture!.Dispose();
        target.Destroy();
        source.Destroy();
        vk.DestroyImage(device.Device, image, null);
        vk.FreeMemory(device.Device, memory, null);
    }
}
