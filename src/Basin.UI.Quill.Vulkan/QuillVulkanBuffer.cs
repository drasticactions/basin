using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Basin.UI.Quill;

internal sealed unsafe class QuillVulkanBuffer
{
    private QuillVulkanBuffer(Buffer buffer, DeviceMemory memory, void* mapped, ulong size)
    {
        Handle = buffer;
        Memory = memory;
        Mapped = (byte*)mapped;
        Size = size;
    }

    internal Buffer Handle { get; }

    internal DeviceMemory Memory { get; }

    internal byte* Mapped { get; }

    internal ulong Size { get; }

    internal static QuillVulkanBuffer Create(VulkanDevice device, ulong size, BufferUsageFlags usage)
    {
        var vk = device.Api;
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanDevice.Check(vk.CreateBuffer(device.Device, in info, null, out var buffer), "vkCreateBuffer(quill)");
        vk.GetBufferMemoryRequirements(device.Device, buffer, out var requirements);
        if (!device.TryMemoryTypeFor(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out var type))
        {
            vk.DestroyBuffer(device.Device, buffer, null);
            throw new InvalidOperationException("no host-visible coherent memory for a quill buffer");
        }

        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = type,
        };
        var result = vk.AllocateMemory(device.Device, in allocate, null, out var memory);
        if (result != Result.Success)
        {
            vk.DestroyBuffer(device.Device, buffer, null);
            VulkanDevice.Check(result, "vkAllocateMemory(quill)");
        }

        void* mapped;
        result = vk.BindBufferMemory(device.Device, buffer, memory, 0);
        if (result == Result.Success)
        {
            result = vk.MapMemory(device.Device, memory, 0, size, 0, &mapped);
        }
        else
        {
            mapped = null;
        }

        if (result != Result.Success)
        {
            vk.FreeMemory(device.Device, memory, null);
            vk.DestroyBuffer(device.Device, buffer, null);
            VulkanDevice.Check(result, "vkMapMemory(quill)");
        }

        return new QuillVulkanBuffer(buffer, memory, mapped, size);
    }

    internal void Destroy(VulkanDevice device)
    {
        var vk = device.Api;
        vk.UnmapMemory(device.Device, Memory);
        vk.DestroyBuffer(device.Device, Handle, null);
        vk.FreeMemory(device.Device, Memory, null);
    }
}
