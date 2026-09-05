using Basin.Color;
using Basin.Diagnostics;
using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanColorTransform : IDisposable
{
    private const int UniformBytes = 6 * 16;

    private readonly VulkanRenderer _renderer;
    private readonly Silk.NET.Vulkan.Buffer _buffer;
    private readonly DeviceMemory _memory;
    private readonly DescriptorAllocation _allocation;

    internal VulkanColorTransform(VulkanRenderer renderer, in ColorTransformParameters parameters)
    {
        _renderer = renderer;
        Parameters = parameters;
        var vk = renderer.Dev.Api;
        var device = renderer.Dev.Device;
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = UniformBytes,
            Usage = BufferUsageFlags.UniformBufferBit,
        };
        VulkanDevice.Check(vk.CreateBuffer(device, in bufferInfo, null, out _buffer), "vkCreateBuffer(color)");
        vk.GetBufferMemoryRequirements(device, _buffer, out var requirements);
        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = renderer.Dev.MemoryTypeFor(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        VulkanDevice.Check(vk.AllocateMemory(device, in allocateInfo, null, out _memory), "vkAllocateMemory(color)");
        VulkanDevice.Check(vk.BindBufferMemory(device, _buffer, _memory, 0), "vkBindBufferMemory(color)");

        void* mapped;
        VulkanDevice.Check(vk.MapMemory(device, _memory, 0, UniformBytes, 0, &mapped), "vkMapMemory(color)");
        var floats = (float*)mapped;
        var m = parameters.Matrix;
        for (var row = 0; row < 3; row++)
        {
            floats[row * 4 + 0] = (float)m[row * 3 + 0];
            floats[row * 4 + 1] = (float)m[row * 3 + 1];
            floats[row * 4 + 2] = (float)m[row * 3 + 2];
            floats[row * 4 + 3] = 0f;
        }

        floats[12] = (int)parameters.Source.Kind;
        floats[13] = (float)parameters.Source.Gamma;
        floats[14] = (float)parameters.Source.MaxLuminance;
        floats[15] = (float)parameters.Anchor;
        floats[16] = (int)parameters.Output.Kind;
        floats[17] = (float)parameters.Output.Gamma;
        floats[18] = (float)parameters.Output.MaxLuminance;
        floats[19] = 0f;
        floats[20] = parameters.MapTones ? 1f : 0f;
        floats[21] = (float)parameters.ToneSourceMax;
        floats[22] = (float)parameters.ToneTargetMax;
        floats[23] = (float)parameters.ToneKnee;
        vk.UnmapMemory(device, _memory);

        _allocation = renderer.ColorDescriptors.Allocate(renderer.ColorSetLayout);
        var info = new DescriptorBufferInfo(_buffer, 0, UniformBytes);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _allocation.Set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &info,
        };
        vk.UpdateDescriptorSets(device, 1, in write, 0, null);
        BasinCounters.Track();
    }

    internal ColorTransformParameters Parameters { get; }

    internal DescriptorSet Set => _allocation.Set;

    public void Dispose()
    {
        var vk = _renderer.Dev.Api;
        _renderer.ColorDescriptors.Free(_allocation);
        vk.DestroyBuffer(_renderer.Dev.Device, _buffer, null);
        vk.FreeMemory(_renderer.Dev.Device, _memory, null);
        BasinCounters.Untrack();
    }
}
