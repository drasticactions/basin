using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.Render3D.Vulkan;

internal sealed unsafe class VulkanWorldTarget
{
    private VulkanWorldTarget(VulkanDeviceImage color, Image depth, DeviceMemory depthMemory, ImageView depthView, Framebuffer framebuffer, DmabufAttributes attributes)
    {
        Color = color;
        Depth = depth;
        DepthMemory = depthMemory;
        DepthView = depthView;
        Framebuffer = framebuffer;
        Attributes = attributes;
    }

    public VulkanDeviceImage Color { get; }

    public Image Depth { get; }

    public DeviceMemory DepthMemory { get; }

    public ImageView DepthView { get; }

    public Framebuffer Framebuffer { get; }

    public DmabufAttributes Attributes { get; }

    public ulong LastPoint { get; set; }

    public static VulkanWorldTarget? TryCreate(VulkanDevice device, VulkanWorldPipelines pipelines, IBuffer buffer, out string reason)
    {
        if (!buffer.TryGetDmabuf(out var attributes))
        {
            reason = "the Vulkan world pass draws only into a dmabuf";
            return null;
        }

        if (VulkanDeviceImage.TryImport(device, attributes, ImageUsageFlags.ColorAttachmentBit) is not { } color)
        {
            reason = $"{device.DevicePath} would not import a {buffer.Width}x{buffer.Height} world buffer as a color attachment";
            return null;
        }

        reason = string.Empty;
        var vk = device.Api;
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = pipelines.DepthFormat,
            Extent = new Extent3D((uint)buffer.Width, (uint)buffer.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(device.Device, in info, null, out var depth), "vkCreateImage(world depth)");
        vk.GetImageMemoryRequirements(device.Device, depth, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocate, null, out var memory), "vkAllocateMemory(world depth)");
        VulkanDevice.Check(vk.BindImageMemory(device.Device, depth, memory, 0), "vkBindImageMemory(world depth)");
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = depth,
            ViewType = ImageViewType.Type2D,
            Format = pipelines.DepthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(device.Device, in viewInfo, null, out var depthView), "vkCreateImageView(world depth)");
        var views = stackalloc ImageView[2] { color.View, depthView };
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = pipelines.MainPass,
            AttachmentCount = 2,
            PAttachments = views,
            Width = (uint)buffer.Width,
            Height = (uint)buffer.Height,
            Layers = 1,
        };
        VulkanDevice.Check(vk.CreateFramebuffer(device.Device, in framebufferInfo, null, out var framebuffer), "vkCreateFramebuffer(world)");
        return new VulkanWorldTarget(color, depth, memory, depthView, framebuffer, attributes);
    }

    public void Destroy(VulkanDevice device)
    {
        var vk = device.Api;
        vk.DestroyFramebuffer(device.Device, Framebuffer, null);
        vk.DestroyImageView(device.Device, DepthView, null);
        vk.DestroyImage(device.Device, Depth, null);
        vk.FreeMemory(device.Device, DepthMemory, null);
        Color.Dispose();
    }
}
