using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

internal sealed unsafe class QuillVulkanFrame : IDisposable
{
    private const uint FirstPoolSets = 32;

    private readonly VulkanDevice _device;
    private readonly QuillVulkanPipeline _pipeline;
    private readonly List<DescriptorPool> _pools = [];
    private readonly List<QuillVulkanBuffer> _stale = [];
    private int _pool;
    private uint _lastPoolSets;
    private QuillVulkanBuffer? _uniforms;
    private QuillVulkanBuffer? _vertices;
    private QuillVulkanBuffer? _indices;
    private ulong _uniformUsed;
    private ulong _vertexUsed;
    private ulong _indexUsed;

    internal QuillVulkanFrame(VulkanDevice device, QuillVulkanPipeline pipeline)
    {
        _device = device;
        _pipeline = pipeline;
    }

    internal ulong Point { get; set; }

    internal bool IsIdle => Point == 0 || _device.IsComplete(Point);

    internal int DescriptorPools => _pools.Count;

    internal void Reset()
    {
        foreach (var buffer in _stale)
        {
            buffer.Destroy(_device);
        }

        _stale.Clear();
        foreach (var pool in _pools)
        {
            _ = _device.Api.ResetDescriptorPool(_device.Device, pool, 0);
        }

        _pool = 0;
        _uniformUsed = 0;
        _vertexUsed = 0;
        _indexUsed = 0;
    }

    internal QuillVulkanBuffer Uniforms(int draws, out ulong offset) =>
        Reserve(ref _uniforms, ref _uniformUsed, (ulong)draws * _pipeline.UniformStride, _pipeline.UniformStride,
            BufferUsageFlags.UniformBufferBit, out offset);

    internal QuillVulkanBuffer Vertices(int bytes, out ulong offset) =>
        Reserve(ref _vertices, ref _vertexUsed, (ulong)bytes, 20, BufferUsageFlags.VertexBufferBit, out offset);

    internal QuillVulkanBuffer Indices(int bytes, out ulong offset) =>
        Reserve(ref _indices, ref _indexUsed, (ulong)bytes, 4, BufferUsageFlags.IndexBufferBit, out offset);

    internal DescriptorSet AllocateSet()
    {
        var layout = _pipeline.SetLayout;
        while (true)
        {
            if (_pool == _pools.Count)
            {
                _pools.Add(CreatePool());
            }

            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pools[_pool],
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            var result = _device.Api.AllocateDescriptorSets(_device.Device, in allocateInfo, out var set);
            if (result == Result.Success)
            {
                return set;
            }

            if (result is not (Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool))
            {
                VulkanDevice.Check(result, "vkAllocateDescriptorSets(quill)");
            }

            _pool++;
        }
    }

    public void Dispose()
    {
        Reset();
        foreach (var pool in _pools)
        {
            _device.Api.DestroyDescriptorPool(_device.Device, pool, null);
        }

        _pools.Clear();
        _uniforms?.Destroy(_device);
        _vertices?.Destroy(_device);
        _indices?.Destroy(_device);
        _uniforms = null;
        _vertices = null;
        _indices = null;
    }

    private QuillVulkanBuffer Reserve(
        ref QuillVulkanBuffer? buffer, ref ulong used, ulong bytes, ulong alignment, BufferUsageFlags usage,
        out ulong offset)
    {
        var start = (used + alignment - 1) / alignment * alignment;
        if (buffer is null || buffer.Size - start < bytes)
        {
            var capacity = Math.Max(Math.Max(bytes, 64UL * 1024), buffer is null ? 0 : buffer.Size * 2);
            if (buffer is not null)
            {
                _stale.Add(buffer);
            }

            buffer = QuillVulkanBuffer.Create(_device, capacity, usage);
            start = 0;
        }

        used = start + bytes;
        offset = start;
        return buffer;
    }

    private DescriptorPool CreatePool()
    {
        var sets = _lastPoolSets == 0 ? FirstPoolSets : _lastPoolSets * 2;
        _lastPoolSets = sets;
        var sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, sets);
        sizes[1] = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, sets * 3);
        var info = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = sets,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
        };
        VulkanDevice.Check(_device.Api.CreateDescriptorPool(_device.Device, in info, null, out var pool), "vkCreateDescriptorPool(quill)");
        return pool;
    }
}
