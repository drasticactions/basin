using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Basin.Render3D.Vulkan;

internal sealed unsafe class VulkanWorldBuffer
{
    private VulkanWorldBuffer(Buffer buffer, DeviceMemory memory, byte* mapped, ulong size)
    {
        Handle = buffer;
        Memory = memory;
        Mapped = mapped;
        Size = size;
    }

    public Buffer Handle { get; }

    public DeviceMemory Memory { get; }

    public byte* Mapped { get; }

    public ulong Size { get; }

    public static VulkanWorldBuffer Create(VulkanDevice device, ulong size, BufferUsageFlags usage)
    {
        var vk = device.Api;
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanDevice.Check(vk.CreateBuffer(device.Device, in info, null, out var buffer), "vkCreateBuffer(world)");
        vk.GetBufferMemoryRequirements(device.Device, buffer, out var requirements);
        if (!device.TryMemoryTypeFor(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out var type))
        {
            vk.DestroyBuffer(device.Device, buffer, null);
            throw new InvalidOperationException("The device has no host-visible coherent memory for a world buffer.");
        }

        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = type,
        };
        VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocate, null, out var memory), "vkAllocateMemory(world buffer)");
        VulkanDevice.Check(vk.BindBufferMemory(device.Device, buffer, memory, 0), "vkBindBufferMemory(world)");
        void* mapped;
        VulkanDevice.Check(vk.MapMemory(device.Device, memory, 0, size, 0, &mapped), "vkMapMemory(world)");
        return new VulkanWorldBuffer(buffer, memory, (byte*)mapped, size);
    }

    public void Destroy(VulkanDevice device)
    {
        var vk = device.Api;
        vk.UnmapMemory(device.Device, Memory);
        vk.DestroyBuffer(device.Device, Handle, null);
        vk.FreeMemory(device.Device, Memory, null);
    }
}
