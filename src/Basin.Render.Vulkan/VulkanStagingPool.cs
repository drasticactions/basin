using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanStagingPool : IDisposable
{
    private const ulong MinBufferSize = 1024 * 1024;
    private const ulong MaxBufferSize = 256 * MinBufferSize;

    private sealed class SharedBuffer
    {
        public Silk.NET.Vulkan.Buffer Buffer;
        public DeviceMemory Memory;
        public void* Mapped;
        public ulong Size;
        public ulong Used;

        public ulong LastUsedPoint;

        public ulong IdleSince;
    }

    private readonly VulkanDevice _device;
    private readonly List<SharedBuffer> _buffers = [];

    internal Action<Silk.NET.Vulkan.Buffer>? BufferDestroyed;

    public VulkanStagingPool(VulkanDevice device) => _device = device;

    public void Recycle(ulong completed)
    {
        _recycles++;
        for (var i = _buffers.Count - 1; i >= 0; i--)
        {
            var buffer = _buffers[i];
            if (buffer.Used > 0)
            {
                if (buffer.LastUsedPoint > completed)
                {
                    continue;
                }

                buffer.Used = 0;
                buffer.IdleSince = _recycles;
                continue;
            }

            if (_buffers.Count > 1 && _recycles - buffer.IdleSince > IdleRecyclesBeforeRelease)
            {
                Destroy(buffer);
                _buffers.RemoveAt(i);
            }
        }
    }

    private const ulong IdleRecyclesBeforeRelease = 600;

    private ulong _recycles;

    private bool TryTake(ulong size, ulong alignment, out StagingSpan span)
    {
        for (var i = _buffers.Count - 1; i >= 0; i--)
        {
            var candidate = _buffers[i];
            var start = Align(candidate.Used, alignment);
            if (candidate.Size - start < size)
            {
                continue;
            }

            if (candidate.Used == 0)
            {
                candidate.LastUsedPoint = ulong.MaxValue;
            }

            candidate.Used = start + size;
            span = new StagingSpan(candidate.Buffer, start, (byte*)candidate.Mapped + start);
            return true;
        }

        span = default;
        return false;
    }

    public StagingSpan Allocate(ulong size, ulong alignment)
    {
        if (TryTake(size, alignment, out var span))
        {
            return span;
        }

        Recycle(_device.Ring.ReadCompleted());
        if (TryTake(size, alignment, out span))
        {
            return span;
        }

        if (size > MaxBufferSize)
        {
            return default;
        }

        var bufferSize = Math.Max(size * 2, MinBufferSize);
        if (_buffers.Count > 0)
        {
            bufferSize = Math.Max(bufferSize, _buffers[^1].Size * 2);
        }

        bufferSize = Math.Min(bufferSize, MaxBufferSize);
        var fresh = Create(bufferSize);
        if (fresh is null)
        {
            return default;
        }

        _buffers.Add(fresh);
        fresh.Used = size;
        fresh.LastUsedPoint = ulong.MaxValue;
        return new StagingSpan(fresh.Buffer, 0, fresh.Mapped);
    }

    public void MarkSubmitted(ulong point)
    {
        foreach (var buffer in _buffers)
        {
            if (buffer.Used > 0)
            {
                buffer.LastUsedPoint = point;
            }
        }
    }

    private static ulong Align(ulong offset, ulong alignment) =>
        (offset + alignment - 1) / alignment * alignment;

    private SharedBuffer? Create(ulong size)
    {
        var vk = _device.Api;
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.VertexBufferBit | BufferUsageFlags.UniformBufferBit,
        };
        if (vk.CreateBuffer(_device.Device, in info, null, out var buffer) != Result.Success)
        {
            return null;
        }

        vk.GetBufferMemoryRequirements(_device.Device, buffer, out var requirements);
        if (!_device.TryMemoryTypeFor(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out var type))
        {
            vk.DestroyBuffer(_device.Device, buffer, null);
            return null;
        }

        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = type,
        };
        if (vk.AllocateMemory(_device.Device, in allocate, null, out var memory) != Result.Success)
        {
            vk.DestroyBuffer(_device.Device, buffer, null);
            return null;
        }

        void* mapped;
        if (vk.BindBufferMemory(_device.Device, buffer, memory, 0) != Result.Success ||
            vk.MapMemory(_device.Device, memory, 0, size, 0, &mapped) != Result.Success)
        {
            vk.FreeMemory(_device.Device, memory, null);
            vk.DestroyBuffer(_device.Device, buffer, null);
            return null;
        }

        return new SharedBuffer
        {
            Buffer = buffer,
            Memory = memory,
            Mapped = mapped,
            Size = size,
        };
    }

    private void Destroy(SharedBuffer buffer)
    {
        BufferDestroyed?.Invoke(buffer.Buffer);
        var vk = _device.Api;
        vk.UnmapMemory(_device.Device, buffer.Memory);
        vk.DestroyBuffer(_device.Device, buffer.Buffer, null);
        vk.FreeMemory(_device.Device, buffer.Memory, null);
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            Destroy(buffer);
        }

        _buffers.Clear();
    }
}
