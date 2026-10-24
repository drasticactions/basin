using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Owns a device-local image that mirrors a CPU-readable buffer. It copies only the damaged regions through the staging pool.
/// </summary>
public sealed unsafe class VulkanUploadImage : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly DrmFormat _format;
    private IBuffer _buffer;
    private readonly DeviceMemory _imageMemory;
    private readonly int _bytesPerPixel;
    private StagingSpan _span;
    private bool _uploaded;
    private bool _transitioned;

    private DamageRects _dirty;
    private CopyRegions _copies;
    private int _copyCount;

    private struct CopyRegion
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public ulong Offset;
    }

    [System.Runtime.CompilerServices.InlineArray(DamageRects.Capacity)]
    private struct CopyRegions
    {
        private CopyRegion _element0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanUploadImage"/> class. It throws <see cref="InvalidOperationException"/> when the buffer is not CPU-readable, its format is not uploadable, it is too large, or a Vulkan step fails.
    /// On an exception, it frees the views, the image and the memory that it made.
    /// </summary>
    /// <param name="device">The device that owns the image.</param>
    /// <param name="buffer">The buffer to mirror. The whole buffer is marked dirty.</param>
    public VulkanUploadImage(VulkanDevice device, IBuffer buffer)
    {
        _device = device;
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
        var vk = device.Api;

        if (!buffer.BeginDataAccess(BufferDataAccess.Read, out var data))
        {
            throw new InvalidOperationException("buffer is not CPU-readable");
        }

        DrmFormat format;
        try
        {
            format = data.Format;
        }
        finally
        {
            buffer.EndDataAccess();
        }

        if (!device.FormatTable.TryGet(format, out var props) || !props.ShmSupported)
        {
            throw new InvalidOperationException($"{format} is not uploadable on this device");
        }

        if ((uint)Width > props.ShmMaxWidth || (uint)Height > props.ShmMaxHeight)
        {
            throw new InvalidOperationException(
                $"{Width}x{Height} exceeds the device's {props.ShmMaxWidth}x{props.ShmMaxHeight} limit for {format}");
        }

        _format = format;
        _bytesPerPixel = format.BytesPerPixel();
        HasAlpha = format.HasAlpha();
        var vkFormat = props.Entry.Vk;
        VkFormat = vkFormat;

        var mutableSrgb = props.ShmHasMutableSrgb && props.Entry.HasSrgb;
        var viewFormats = stackalloc Format[2] { props.Entry.Vk, props.Entry.VkSrgb };
        var formatList = new ImageFormatListCreateInfo
        {
            SType = StructureType.ImageFormatListCreateInfo,
            ViewFormatCount = 2,
            PViewFormats = viewFormats,
        };
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = mutableSrgb ? &formatList : null,
            Flags = mutableSrgb ? ImageCreateFlags.CreateMutableFormatBit : 0,
            ImageType = ImageType.Type2D,
            Format = vkFormat,
            Extent = new Extent3D((uint)Width, (uint)Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined,
        };
        try
        {
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out var image), "vkCreateImage(upload)");
            Image = image;
            vk.GetImageMemoryRequirements(device.Device, Image, out var requirements);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out _imageMemory), "vkAllocateMemory(upload image)");
            VulkanDevice.Check(vk.BindImageMemory(device.Device, Image, _imageMemory, 0), "vkBindImageMemory");

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = Image,
                ViewType = ImageViewType.Type2D,
                Format = vkFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(vk.CreateImageView(device.Device, in viewInfo, null, out var view), "vkCreateImageView(upload)");
            View = view;

            if (mutableSrgb)
            {
                var srgbViewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = Image,
                    ViewType = ImageViewType.Type2D,
                    Format = props.Entry.VkSrgb,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                };
                VulkanDevice.Check(vk.CreateImageView(device.Device, in srgbViewInfo, null, out var srgbView), "vkCreateImageView(upload srgb)");
                SrgbView = srgbView;
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        NeedsShaderDecode = props.Entry.NeedsShaderDecode(mutableSrgb);

        MarkDirty();
    }

    /// <summary>
    /// Gets the device-local image.
    /// </summary>
    public Image Image { get; }

    /// <summary>
    /// Gets the Vulkan format of <see cref="Image"/> and <see cref="View"/>.
    /// </summary>
    public Format VkFormat { get; }

    /// <summary>
    /// Gets a view that returns the stored values without sRGB decoding.
    /// </summary>
    public ImageView View { get; }

    /// <summary>
    /// Gets a view that decodes sRGB on sampling, or a null handle when the device cannot make one.
    /// </summary>
    public ImageView SrgbView { get; }

    /// <summary>
    /// Gets a value indicating whether a shader must decode sRGB itself, because <see cref="SrgbView"/> is absent.
    /// </summary>
    public bool NeedsShaderDecode { get; }

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the format of the buffer has an alpha channel.
    /// </summary>
    public bool HasAlpha { get; }

    /// <summary>
    /// Points the image at a new buffer of the same size and format, and marks its damage dirty.
    /// </summary>
    /// <param name="source">The buffer that replaces the current one.</param>
    /// <param name="damage">The changed region in buffer pixels.</param>
    /// <returns><see langword="false"/> when the size or format differs or the buffer is not CPU-readable.</returns>
    public bool TryAdopt(IBuffer source, in Box damage)
    {
        if (source.Width != Width || source.Height != Height)
        {
            return false;
        }

        if (!source.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        var format = view.Format;
        source.EndDataAccess();
        if (format != _format)
        {
            return false;
        }

        _buffer = source;
        MarkDirty(damage);
        return true;
    }

    /// <summary>
    /// Marks the whole image for upload.
    /// </summary>
    public void MarkDirty()
    {
        _uploaded = false;
        _dirty.Clear();
        _dirty.Add(0, 0, Width, Height);
    }

    /// <summary>
    /// Adds a region to the next upload. A region outside the image is ignored.
    /// </summary>
    /// <param name="damage">The changed region in buffer pixels.</param>
    public void MarkDirty(in Box damage)
    {
        var x0 = Math.Clamp(damage.X, 0, Width);
        var y0 = Math.Clamp(damage.Y, 0, Height);
        var x1 = Math.Clamp(damage.X + damage.Width, 0, Width);
        var y1 = Math.Clamp(damage.Y + damage.Height, 0, Height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        if (_uploaded)
        {
            _dirty.Clear();
            _uploaded = false;
        }

        _dirty.Add(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Gets the number of pixel bytes that the image copied into staging since it was created.
    /// </summary>
    public ulong UploadedBytes { get; private set; }

    private ulong AlignRegion(ulong bytes)
    {
        var alignment = (ulong)(_bytesPerPixel * 4);
        return (bytes + alignment - 1) / alignment * alignment;
    }

    /// <summary>
    /// Copies the dirty regions into a staging range and sets <see cref="NeedsGpuCopy"/>. It does nothing when the image is up to date.
    /// </summary>
    /// <param name="staging">The pool that supplies the staging range.</param>
    /// <returns><see langword="false"/> when staging space or a CPU read of the buffer was not available.</returns>
    public bool PrepareUpload(VulkanStagingPool staging)
    {
        if (_uploaded)
        {
            return true;
        }

        var count = _dirty.Count;
        if (count == 0)
        {
            _uploaded = true;
            return true;
        }

        ulong total = 0;
        for (var i = 0; i < count; i++)
        {
            var box = _dirty[i];
            total += AlignRegion((ulong)(box.Width * _bytesPerPixel) * (ulong)box.Height);
        }

        var span = staging.Allocate(total, (ulong)(_bytesPerPixel * 4));
        if (!span.IsValid)
        {
            return _uploaded;
        }

        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return _uploaded;
        }

        try
        {
            ulong offset = 0;
            for (var i = 0; i < count; i++)
            {
                var box = _dirty[i];
                var rowBytes = (ulong)(box.Width * _bytesPerPixel);
                var source = view.Data + (nint)box.Y * view.Stride + (nint)(box.X * _bytesPerPixel);
                var destination = (byte*)span.Mapped + offset;

                if (box.Width == Width && (ulong)view.Stride == rowBytes)
                {
                    System.Buffer.MemoryCopy((void*)source, destination, rowBytes * (ulong)box.Height, rowBytes * (ulong)box.Height);
                }
                else
                {
                    for (var row = 0; row < box.Height; row++)
                    {
                        System.Buffer.MemoryCopy(
                            (void*)(source + (nint)row * view.Stride),
                            destination + (ulong)row * rowBytes,
                            rowBytes,
                            rowBytes);
                    }
                }

                _copies[i] = new CopyRegion
                {
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height,
                    Offset = offset,
                };
                offset += AlignRegion(rowBytes * (ulong)box.Height);
                UploadedBytes += rowBytes * (ulong)box.Height;
            }

            _span = span;
            _copyCount = count;
            NeedsGpuCopy = true;
            _uploaded = true;
            return true;
        }
        finally
        {
            _buffer.EndDataAccess();
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="RecordGpuCopy"/> has staged data to copy.
    /// </summary>
    public bool NeedsGpuCopy { get; private set; }

    /// <summary>
    /// Records the copy from staging into the image and leaves the image in the general layout for shader reads.
    /// </summary>
    /// <param name="commands">The command buffer to record into. It must execute before the image is sampled.</param>
    public void RecordGpuCopy(CommandBuffer commands)
    {
        var vk = _device.Api;
        var toTransfer = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = _transitioned ? AccessFlags.ShaderReadBit : 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = _transitioned ? ImageLayout.General : ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = Image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, &toTransfer);

        var regions = stackalloc BufferImageCopy[DamageRects.Capacity];
        for (var i = 0; i < _copyCount; i++)
        {
            var region = _copies[i];
            regions[i] = new BufferImageCopy
            {
                BufferOffset = _span.Offset + region.Offset,
                BufferRowLength = (uint)region.Width,
                BufferImageHeight = (uint)region.Height,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(region.X, region.Y, 0),
                ImageExtent = new Extent3D((uint)region.Width, (uint)region.Height, 1),
            };
        }

        vk.CmdCopyBufferToImage(commands, _span.Buffer, Image, ImageLayout.TransferDstOptimal, (uint)_copyCount, regions);

        var toGeneral = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = Image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        vk.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &toGeneral);
        _transitioned = true;
        NeedsGpuCopy = false;
    }

    /// <summary>
    /// Destroys the views, the image and its memory. The GPU must be done with the image.
    /// </summary>
    public void Dispose()
    {
        var vk = _device.Api;
        if (SrgbView.Handle != 0)
        {
            vk.DestroyImageView(_device.Device, SrgbView, null);
        }

        vk.DestroyImageView(_device.Device, View, null);
        vk.DestroyImage(_device.Device, Image, null);
        vk.FreeMemory(_device.Device, _imageMemory, null);
    }
}
