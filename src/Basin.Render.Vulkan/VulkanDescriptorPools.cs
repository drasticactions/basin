using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanDescriptorPools : IDisposable
{
    private const uint StartSize = 256;

    private readonly VulkanDevice _device;
    private readonly DescriptorType _type;
    private readonly List<DescriptorPool> _pools = [];
    private uint _lastSize = StartSize / 2;

    public VulkanDescriptorPools(VulkanDevice device, DescriptorType type = DescriptorType.CombinedImageSampler)
    {
        _device = device;
        _type = type;
    }

    public DescriptorAllocation Allocate(DescriptorSetLayout layout)
    {
        foreach (var pool in _pools)
        {
            if (TryAllocate(pool, layout, out var set))
            {
                return new DescriptorAllocation(set, pool);
            }
        }

        _lastSize *= 2;
        var poolSize = new DescriptorPoolSize(_type, _lastSize);
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = _lastSize,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        VulkanDevice.Check(_device.Api.CreateDescriptorPool(_device.Device, in poolInfo, null, out var fresh), "vkCreateDescriptorPool");
        _pools.Add(fresh);
        return TryAllocate(fresh, layout, out var freshSet)
            ? new DescriptorAllocation(freshSet, fresh)
            : throw new InvalidOperationException("a fresh descriptor pool refused its first allocation");
    }

    public void Free(in DescriptorAllocation allocation)
    {
        var set = allocation.Set;
        _ = _device.Api.FreeDescriptorSets(_device.Device, allocation.Pool, 1, in set);
    }

    private bool TryAllocate(DescriptorPool pool, DescriptorSetLayout layout, out DescriptorSet set)
    {
        var allocateInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        var result = _device.Api.AllocateDescriptorSets(_device.Device, in allocateInfo, out set);
        return result switch
        {
            Result.Success => true,
            Result.ErrorFragmentedPool or Result.ErrorOutOfPoolMemory => false,
            _ => throw new InvalidOperationException($"vkAllocateDescriptorSets failed: {result}"),
        };
    }

    public void Dispose()
    {
        foreach (var pool in _pools)
        {
            _device.Api.DestroyDescriptorPool(_device.Device, pool, null);
        }

        _pools.Clear();
    }
}
