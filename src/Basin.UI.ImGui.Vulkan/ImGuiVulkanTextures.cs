using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiVulkanTextures : IDisposable
{
    private const ulong MinArenaSize = 256 * 1024;
    private const ulong CopyAlignment = 16;
    private const uint SetsPerPool = 64;

    internal sealed class Texture
    {
        public required Image Image;
        public required DeviceMemory Memory;
        public required ImageView View;
        public required DescriptorSet Set;
        public required DescriptorPool Pool;
        public int Width;
        public int Height;
        public bool Initialized;
        public int Stamp;
        public ulong RetiredAt;
    }

    private sealed class Arena
    {
        public required ImGuiVulkanBuffer Buffer;
        public ulong Used;
        public ulong Point;
        public bool Unsubmitted;
    }

    private struct Upload
    {
        public Texture Texture;
        public Buffer Source;
        public ulong Offset;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    private readonly VulkanDevice _device;
    private readonly ImGuiVulkanPipeline _pipeline;
    private readonly List<Texture?> _slots = [];
    private readonly List<Texture> _uninitialized = [];
    private readonly List<Texture> _retired = [];
    private readonly List<Texture> _touched = [];
    private readonly List<Arena> _arenas = [];
    private readonly List<Upload> _uploads = [];
    private readonly List<DescriptorPool> _pools = [];
    private int _stamp;
    private ulong _lastSubmitted;
    private bool _disposed;

    internal ImGuiVulkanTextures(VulkanDevice device, ImGuiVulkanPipeline pipeline)
    {
        _device = device;
        _pipeline = pipeline;
    }

    internal int Live
    {
        get
        {
            var live = 0;
            foreach (var slot in _slots)
            {
                live += slot is null ? 0 : 1;
            }

            return live;
        }
    }

    internal bool HasPending => _uploads.Count > 0 || _uninitialized.Count > 0;

    internal Texture? Of(int index) => (uint)index < (uint)_slots.Count ? _slots[index] : null;

    internal int Create(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var texture = Allocate(Math.Max(1, width), Math.Max(1, height));
        var slot = _slots.IndexOf(null);
        if (slot < 0)
        {
            _slots.Add(texture);
            return _slots.Count - 1;
        }

        _slots[slot] = texture;
        return slot;
    }

    internal void Update(int index, int x, int y, int width, int height, byte* pixels, int pitch)
    {
        if (_disposed || Of(index) is not { } texture || width <= 0 || height <= 0 || x < 0 || y < 0
            || x + width > texture.Width || y + height > texture.Height)
        {
            return;
        }

        var row = width * 4;
        var needed = (ulong)(row * height);
        var arena = ArenaFor(needed, out var offset);
        var into = arena.Buffer.Mapped + offset;
        for (var line = 0; line < height; line++)
        {
            System.Buffer.MemoryCopy(pixels + (line * pitch), into + (line * row), row, row);
        }

        arena.Unsubmitted = true;
        _uploads.Add(new Upload
        {
            Texture = texture,
            Source = arena.Buffer.Handle,
            Offset = offset,
            X = x,
            Y = y,
            Width = width,
            Height = height,
        });
    }

    internal void Destroy(int index)
    {
        if (Of(index) is not { } texture)
        {
            return;
        }

        _slots[index] = null;
        _uninitialized.Remove(texture);
        for (var i = _uploads.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_uploads[i].Texture, texture))
            {
                _uploads.RemoveAt(i);
            }
        }

        texture.RetiredAt = _lastSubmitted;
        _retired.Add(texture);
        ReleaseCompleted();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_lastSubmitted > 0)
        {
            _device.WaitFor(_lastSubmitted);
        }

        foreach (var texture in _slots)
        {
            if (texture is not null)
            {
                Free(texture);
            }
        }

        foreach (var texture in _retired)
        {
            Free(texture);
        }

        _slots.Clear();
        _retired.Clear();
        _uninitialized.Clear();
        _uploads.Clear();
        foreach (var arena in _arenas)
        {
            arena.Buffer.Destroy(_device);
        }

        _arenas.Clear();
        foreach (var pool in _pools)
        {
            _device.Api.DestroyDescriptorPool(_device.Device, pool, null);
        }

        _pools.Clear();
    }

    internal void Record(CommandBuffer commands)
    {
        if (!HasPending)
        {
            return;
        }

        var vk = _device.Api;
        _stamp++;
        _touched.Clear();
        foreach (var texture in _uninitialized)
        {
            Touch(texture);
        }

        foreach (var upload in _uploads)
        {
            Touch(upload.Texture);
        }

        var barriers = stackalloc ImageMemoryBarrier[_touched.Count];
        for (var i = 0; i < _touched.Count; i++)
        {
            var texture = _touched[i];
            barriers[i] = Barrier(
                texture.Image,
                texture.Initialized ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                texture.Initialized ? AccessFlags.ShaderReadBit : 0,
                AccessFlags.TransferWriteBit);
        }

        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, (uint)_touched.Count, barriers);

        if (_uninitialized.Count > 0)
        {
            var clear = default(ClearColorValue);
            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            foreach (var texture in _uninitialized)
            {
                vk.CmdClearColorImage(commands, texture.Image, ImageLayout.TransferDstOptimal, &clear, 1, &range);
            }

            var ordered = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.TransferWriteBit,
            };
            vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit,
                0, 1, &ordered, 0, null, 0, null);
        }

        foreach (var upload in _uploads)
        {
            var copy = new BufferImageCopy
            {
                BufferOffset = upload.Offset,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(upload.X, upload.Y, 0),
                ImageExtent = new Extent3D((uint)upload.Width, (uint)upload.Height, 1),
            };
            vk.CmdCopyBufferToImage(commands, upload.Source, upload.Texture.Image, ImageLayout.TransferDstOptimal, 1, &copy);
        }

        for (var i = 0; i < _touched.Count; i++)
        {
            barriers[i] = Barrier(
                _touched[i].Image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit);
            _touched[i].Initialized = true;
        }

        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, (uint)_touched.Count, barriers);

        _uninitialized.Clear();
        _uploads.Clear();
        _touched.Clear();
    }

    internal void MarkSubmitted(ulong point)
    {
        _lastSubmitted = Math.Max(_lastSubmitted, point);
        foreach (var arena in _arenas)
        {
            if (arena.Unsubmitted)
            {
                arena.Unsubmitted = false;
                arena.Point = point;
            }
        }

        ReleaseCompleted();
    }

    private void ReleaseCompleted()
    {
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            var texture = _retired[i];
            if (texture.RetiredAt == 0 || _device.IsComplete(texture.RetiredAt))
            {
                Free(texture);
                _retired.RemoveAt(i);
            }
        }
    }

    private Arena ArenaFor(ulong size, out ulong offset)
    {
        foreach (var arena in _arenas)
        {
            if (arena.Used > 0 && !arena.Unsubmitted && (arena.Point == 0 || _device.IsComplete(arena.Point)))
            {
                arena.Used = 0;
            }

            var start = (arena.Used + CopyAlignment - 1) / CopyAlignment * CopyAlignment;
            if (arena.Buffer.Size - start >= size)
            {
                arena.Used = start + size;
                offset = start;
                return arena;
            }
        }

        var capacity = Math.Max(MinArenaSize, size * 2);
        if (_arenas.Count > 0)
        {
            capacity = Math.Max(capacity, _arenas[^1].Buffer.Size * 2);
        }

        var fresh = new Arena
        {
            Buffer = ImGuiVulkanBuffer.Create(_device, capacity, BufferUsageFlags.TransferSrcBit),
            Used = size,
        };
        _arenas.Add(fresh);
        offset = 0;
        return fresh;
    }

    private void Touch(Texture texture)
    {
        if (texture.Stamp == _stamp)
        {
            return;
        }

        texture.Stamp = _stamp;
        _touched.Add(texture);
    }

    private Texture Allocate(int width, int height)
    {
        var vk = _device.Api;
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out var image), "vkCreateImage(imgui texture)");
        vk.GetImageMemoryRequirements(_device.Device, image, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        var result = vk.AllocateMemory(_device.Device, in allocate, null, out var memory);
        if (result != Result.Success)
        {
            vk.DestroyImage(_device.Device, image, null);
            VulkanDevice.Check(result, "vkAllocateMemory(imgui texture)");
        }

        VulkanDevice.Check(vk.BindImageMemory(_device.Device, image, memory, 0), "vkBindImageMemory(imgui texture)");
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out var view), "vkCreateImageView(imgui texture)");

        var set = AllocateSet(out var pool);
        var imageDescriptor = new DescriptorImageInfo(default, view, ImageLayout.ShaderReadOnlyOptimal);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageDescriptor,
        };
        vk.UpdateDescriptorSets(_device.Device, 1, &write, 0, null);

        var texture = new Texture
        {
            Image = image,
            Memory = memory,
            View = view,
            Set = set,
            Pool = pool,
            Width = width,
            Height = height,
        };
        _uninitialized.Add(texture);
        return texture;
    }

    private DescriptorSet AllocateSet(out DescriptorPool pool)
    {
        var layout = _pipeline.SetLayout;
        for (var i = 0; ; i++)
        {
            if (i == _pools.Count)
            {
                _pools.Add(CreatePool());
            }

            var info = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pools[i],
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            var result = _device.Api.AllocateDescriptorSets(_device.Device, in info, out var set);
            if (result == Result.Success)
            {
                pool = _pools[i];
                return set;
            }

            if (result is not (Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool))
            {
                VulkanDevice.Check(result, "vkAllocateDescriptorSets(imgui)");
            }
        }
    }

    private DescriptorPool CreatePool()
    {
        var size = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, SetsPerPool);
        var info = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = SetsPerPool,
            PoolSizeCount = 1,
            PPoolSizes = &size,
        };
        VulkanDevice.Check(_device.Api.CreateDescriptorPool(_device.Device, in info, null, out var pool), "vkCreateDescriptorPool(imgui)");
        return pool;
    }

    private void Free(Texture texture)
    {
        var vk = _device.Api;
        var set = texture.Set;
        _ = vk.FreeDescriptorSets(_device.Device, texture.Pool, 1, &set);
        vk.DestroyImageView(_device.Device, texture.View, null);
        vk.DestroyImage(_device.Device, texture.Image, null);
        vk.FreeMemory(_device.Device, texture.Memory, null);
    }

    private static ImageMemoryBarrier Barrier(
        Image image, ImageLayout from, ImageLayout to, AccessFlags srcAccess, AccessFlags dstAccess) => new()
    {
        SType = StructureType.ImageMemoryBarrier,
        SrcAccessMask = srcAccess,
        DstAccessMask = dstAccess,
        OldLayout = from,
        NewLayout = to,
        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
        Image = image,
        SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
    };
}
