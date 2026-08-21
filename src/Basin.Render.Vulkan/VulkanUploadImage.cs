using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

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

    private int _dirtyX0;
    private int _dirtyY0;
    private int _dirtyX1;
    private int _dirtyY1;

    private int _copyX;
    private int _copyY;
    private int _copyWidth;
    private int _copyHeight;

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

        NeedsShaderDecode = props.Entry.NeedsShaderDecode(mutableSrgb);

        MarkDirty();
    }

    public Image Image { get; }

    public ImageView View { get; }

    public ImageView SrgbView { get; }

    public bool NeedsShaderDecode { get; }

    public int Width { get; }

    public int Height { get; }

    public bool HasAlpha { get; }

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

    public void MarkDirty()
    {
        _uploaded = false;
        _dirtyX0 = 0;
        _dirtyY0 = 0;
        _dirtyX1 = Width;
        _dirtyY1 = Height;
    }

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
            _dirtyX0 = x0;
            _dirtyY0 = y0;
            _dirtyX1 = x1;
            _dirtyY1 = y1;
            _uploaded = false;
            return;
        }

        _dirtyX0 = Math.Min(_dirtyX0, x0);
        _dirtyY0 = Math.Min(_dirtyY0, y0);
        _dirtyX1 = Math.Max(_dirtyX1, x1);
        _dirtyY1 = Math.Max(_dirtyY1, y1);
    }

    public bool PrepareUpload(VulkanStagingPool staging)
    {
        if (_uploaded)
        {
            return true;
        }

        var x = _dirtyX0;
        var y = _dirtyY0;
        var width = _dirtyX1 - _dirtyX0;
        var height = _dirtyY1 - _dirtyY0;
        var rowBytes = (ulong)(width * _bytesPerPixel);
        var span = staging.Allocate(rowBytes * (ulong)height, (ulong)_bytesPerPixel);
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
            var source = view.Data + (nint)y * view.Stride + (nint)(x * _bytesPerPixel);

            if (width == Width && (ulong)view.Stride == rowBytes)
            {
                System.Buffer.MemoryCopy((void*)source, span.Mapped, rowBytes * (ulong)height, rowBytes * (ulong)height);
            }
            else
            {
                for (var row = 0; row < height; row++)
                {
                    System.Buffer.MemoryCopy(
                        (void*)(source + (nint)row * view.Stride),
                        (byte*)span.Mapped + (ulong)row * rowBytes,
                        rowBytes,
                        rowBytes);
                }
            }

            _span = span;
            _copyX = x;
            _copyY = y;
            _copyWidth = width;
            _copyHeight = height;
            NeedsGpuCopy = true;
            _uploaded = true;
            return true;
        }
        finally
        {
            _buffer.EndDataAccess();
        }
    }

    public bool NeedsGpuCopy { get; private set; }

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

        var copy = new BufferImageCopy
        {
            BufferOffset = _span.Offset,
            BufferRowLength = (uint)_copyWidth,
            BufferImageHeight = (uint)_copyHeight,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(_copyX, _copyY, 0),
            ImageExtent = new Extent3D((uint)_copyWidth, (uint)_copyHeight, 1),
        };
        vk.CmdCopyBufferToImage(commands, _span.Buffer, Image, ImageLayout.TransferDstOptimal, 1, &copy);

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
