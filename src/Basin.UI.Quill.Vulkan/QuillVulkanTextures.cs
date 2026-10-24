using Basin.Render.Vulkan;
using Pixman;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Basin.UI.Quill;

/// <summary>
/// The pool of RGBA Vulkan textures that the surfaces of a <see cref="QuillVulkanUIHost"/> share. Uploads go through staging arenas and run in the next frame.
/// </summary>
public sealed unsafe class QuillVulkanTextures : IDisposable
{
    private const ulong MinArenaSize = 256 * 1024;
    private const ulong CopyAlignment = 16;

    private sealed class Arena
    {
        public required QuillVulkanBuffer Buffer;
        public ulong Used;
        public ulong Point;
        public bool Unsubmitted;
    }

    private struct Upload
    {
        public QuillVulkanTexture Texture;
        public Buffer Source;
        public ulong Offset;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    private readonly VulkanDevice _device;
    private readonly List<QuillVulkanTexture> _textures = [];
    private readonly List<QuillVulkanTexture> _uninitialized = [];
    private readonly List<QuillVulkanTexture> _retired = [];
    private readonly List<QuillVulkanTexture> _touched = [];
    private readonly List<Arena> _arenas = [];
    private readonly List<Upload> _uploads = [];
    private int _stamp;
    private int _openFrames;
    private ulong _lastSubmitted;
    private bool _disposed;

    internal QuillVulkanTextures(VulkanDevice device)
    {
        _device = device;
        White = Allocate(1, 1);
        ReadOnlySpan<byte> opaque = [255, 255, 255, 255];
        Update(White, new Box(0, 0, 1, 1), opaque);
    }

    /// <summary>
    /// Gets the number of live textures in the pool.
    /// </summary>
    public int Count => _textures.Count;

    internal QuillVulkanTexture White { get; }

    internal bool HasPending => _uploads.Count > 0 || _uninitialized.Count > 0;

    /// <summary>
    /// Creates an RGBA texture. Its contents are not defined until an upload runs.
    /// </summary>
    /// <param name="width">The width in texels. A value below 1 becomes 1.</param>
    /// <param name="height">The height in texels. A value below 1 becomes 1.</param>
    /// <returns>The texture, which the pool owns until <see cref="Destroy"/>.</returns>
    /// <exception cref="ObjectDisposedException">The pool is disposed.</exception>
    public QuillVulkanTexture Create(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var texture = Allocate(Math.Max(1, width), Math.Max(1, height));
        _textures.Add(texture);
        return texture;
    }

    /// <summary>
    /// Copies RGBA pixels into a staging arena and queues their upload into a part of a texture. The call does nothing when the part is empty or outside the texture, the data is too short, or the pool is disposed.
    /// </summary>
    /// <param name="texture">The texture to write.</param>
    /// <param name="bounds">The part of the texture, in texels.</param>
    /// <param name="rgba">The tightly packed RGBA bytes of the part. The pool copies them before it returns.</param>
    public void Update(QuillVulkanTexture texture, in Box bounds, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_disposed || bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < 0 || bounds.Y < 0
            || bounds.X + bounds.Width > texture.Width || bounds.Y + bounds.Height > texture.Height)
        {
            return;
        }

        var needed = bounds.Width * bounds.Height * 4;
        if (rgba.Length < needed)
        {
            return;
        }

        var arena = ArenaFor((ulong)needed, out var offset);
        rgba[..needed].CopyTo(new Span<byte>(arena.Buffer.Mapped + offset, needed));
        arena.Unsubmitted = true;
        _uploads.Add(new Upload
        {
            Texture = texture,
            Source = arena.Buffer.Handle,
            Offset = offset,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        });
    }

    /// <summary>
    /// Removes a texture from the pool and drops its queued uploads. The pool frees the image after the GPU finishes the frames that use it.
    /// </summary>
    /// <param name="texture">The texture to destroy. A texture that the pool does not hold is ignored.</param>
    public void Destroy(QuillVulkanTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!_textures.Remove(texture))
        {
            return;
        }

        _uninitialized.Remove(texture);
        _uploads.RemoveAll(upload => ReferenceEquals(upload.Texture, texture));
        texture.RetiredAt = _openFrames > 0 ? ulong.MaxValue : _lastSubmitted;
        _retired.Add(texture);
        ReleaseCompleted();
    }

    /// <summary>
    /// Waits for the last submitted frame, then frees every texture and staging arena.
    /// </summary>
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

        foreach (var texture in _textures)
        {
            Free(texture);
        }

        foreach (var texture in _retired)
        {
            Free(texture);
        }

        Free(White);
        _textures.Clear();
        _retired.Clear();
        _uninitialized.Clear();
        _uploads.Clear();
        foreach (var arena in _arenas)
        {
            arena.Buffer.Destroy(_device);
        }

        _arenas.Clear();
    }

    internal void OpenFrame() => _openFrames++;

    internal void CloseFrame() => _openFrames = Math.Max(0, _openFrames - 1);

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
            if (arena.Unsubmitted && !HasUploadsFrom(arena))
            {
                arena.Unsubmitted = false;
                arena.Point = point;
            }
        }

        foreach (var texture in _retired)
        {
            if (texture.RetiredAt == ulong.MaxValue && _openFrames == 0)
            {
                texture.RetiredAt = point;
            }
        }

        ReleaseCompleted();
    }

    private void ReleaseCompleted()
    {
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            var texture = _retired[i];
            if (texture.RetiredAt != ulong.MaxValue && (texture.RetiredAt == 0 || _device.IsComplete(texture.RetiredAt)))
            {
                Free(texture);
                _retired.RemoveAt(i);
            }
        }
    }

    private bool HasUploadsFrom(Arena arena)
    {
        foreach (var upload in _uploads)
        {
            if (upload.Source.Handle == arena.Buffer.Handle.Handle)
            {
                return true;
            }
        }

        return false;
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
            Buffer = QuillVulkanBuffer.Create(_device, capacity, BufferUsageFlags.TransferSrcBit),
            Used = size,
        };
        _arenas.Add(fresh);
        offset = 0;
        return fresh;
    }

    private void Touch(QuillVulkanTexture texture)
    {
        if (texture.Stamp == _stamp)
        {
            return;
        }

        texture.Stamp = _stamp;
        _touched.Add(texture);
    }

    private QuillVulkanTexture Allocate(int width, int height)
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
        VulkanDevice.Check(vk.CreateImage(_device.Device, in imageInfo, null, out var image), "vkCreateImage(quill texture)");
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
            VulkanDevice.Check(result, "vkAllocateMemory(quill texture)");
        }

        VulkanDevice.Check(vk.BindImageMemory(_device.Device, image, memory, 0), "vkBindImageMemory(quill texture)");
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        VulkanDevice.Check(vk.CreateImageView(_device.Device, in viewInfo, null, out var view), "vkCreateImageView(quill texture)");

        var texture = new QuillVulkanTexture(image, memory, view, width, height);
        _uninitialized.Add(texture);
        return texture;
    }

    private void Free(QuillVulkanTexture texture)
    {
        var vk = _device.Api;
        vk.DestroyImageView(_device.Device, texture.View, null);
        vk.DestroyImage(_device.Device, texture.Image, null);
        vk.FreeMemory(_device.Device, texture.Memory, null);
        texture.Retire();
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
