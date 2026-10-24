using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.Rashader.Vulkan;

/// <summary>
/// Runs several <see cref="RashaderFilter"/> chains in order inside one <c>Record</c> call. Two
/// intermediate images carry the frame between chains.
/// </summary>
public sealed unsafe class RashaderFilterStack : IVulkanFilter, IDisposable
{
    private readonly RashaderFilter[] _filters;
    private readonly List<RashaderFilter> _live = [];
    private readonly Image[] _images = new Image[2];
    private readonly DeviceMemory[] _memory = new DeviceMemory[2];
    private VulkanDevice? _device;
    private Extent2D _extent;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RashaderFilterStack"/> class. The stack takes
    /// ownership of the filters and disposes them with itself.
    /// </summary>
    /// <param name="filters">The chains in the order they run.</param>
    public RashaderFilterStack(IReadOnlyList<RashaderFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = [.. filters];
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the chains in the order they run, including chains that have failed.
    /// </summary>
    public IReadOnlyList<RashaderFilter> Filters => _filters;

    /// <summary>
    /// Gets a value indicating whether at least one chain in the stack is still live.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            foreach (var filter in _filters)
            {
                if (filter.IsSupported)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the stack needs the whole frame. It is always <see
    /// langword="true"/>.
    /// </summary>
    public bool NeedsFullFrame => true;

    /// <summary>
    /// Gets a value indicating whether any chain in the stack asks for a repaint every frame.
    /// </summary>
    public bool NeedsContinuousRepaint
    {
        get
        {
            foreach (var filter in _filters)
            {
                if (filter.NeedsContinuousRepaint)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Runs every live chain in order. A chain that fails is skipped and its input goes to the next
    /// chain. The stack records its own barriers between chains. The intermediate images follow the
    /// target extent.
    /// </summary>
    /// <param name="context">The source, the final target, the viewport and the frame options.</param>
    /// <returns><see langword="true"/> when the last live chain wrote the final target.</returns>
    public bool Record(in VulkanFilterContext context)
    {
        _live.Clear();
        foreach (var filter in _filters)
        {
            if (filter.IsSupported)
            {
                _live.Add(filter);
            }
        }

        if (_live.Count == 0)
        {
            return false;
        }

        if (_live.Count == 1)
        {
            return _live[0].Record(in context);
        }

        EnsureIntermediates(context.Device, context.TargetExtent);

        var source = context.Source;
        var sourceFormat = context.SourceFormat;
        var sourceExtent = context.SourceExtent;
        var next = 0;
        var wrote = false;
        for (var i = 0; i < _live.Count; i++)
        {
            var last = i == _live.Count - 1;
            var target = last ? context.Target : _images[next];
            if (!last)
            {
                Barrier(
                    context, target,
                    ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
                    0, AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                    PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ColorAttachmentOutputBit);
            }

            var hop = new VulkanFilterContext
            {
                Device = context.Device,
                Commands = context.Commands,
                Source = source,
                SourceFormat = sourceFormat,
                SourceExtent = sourceExtent,
                Target = target,
                TargetFormat = last ? context.TargetFormat : Format.B8G8R8A8Unorm,
                TargetExtent = context.TargetExtent,
                Viewport = last
                    ? context.Viewport
                    : new Box(0, 0, (int)context.TargetExtent.Width, (int)context.TargetExtent.Height),
                Options = context.Options,
            };
            if (!_live[i].Record(in hop))
            {
                continue;
            }

            if (last)
            {
                wrote = true;
                continue;
            }

            Barrier(
                context, target,
                ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.ColorAttachmentWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.FragmentShaderBit);
            source = target;
            sourceFormat = Format.B8G8R8A8Unorm;
            sourceExtent = context.TargetExtent;
            next ^= 1;
        }

        return wrote;
    }

    /// <summary>
    /// Frees the intermediate images and disposes every chain. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        DropIntermediates();
        foreach (var filter in _filters)
        {
            filter.Dispose();
        }
    }

    private static void Barrier(
        in VulkanFilterContext context, Image image,
        ImageLayout oldLayout, ImageLayout newLayout,
        AccessFlags sourceAccess, AccessFlags targetAccess,
        PipelineStageFlags sourceStage, PipelineStageFlags targetStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = sourceAccess,
            DstAccessMask = targetAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        context.Device.Api.CmdPipelineBarrier(
            context.Commands, sourceStage, targetStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private void EnsureIntermediates(VulkanDevice device, Extent2D extent)
    {
        if (_device is not null && _extent.Width == extent.Width && _extent.Height == extent.Height)
        {
            return;
        }

        DropIntermediates();
        var vk = device.Api;
        for (var i = 0; i < 2; i++)
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                Extent = new Extent3D(extent.Width, extent.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out _images[i]), "vkCreateImage(filter stack)");
            vk.GetImageMemoryRequirements(device.Device, _images[i], out var requirements);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out _memory[i]), "vkAllocateMemory(filter stack)");
            VulkanDevice.Check(vk.BindImageMemory(device.Device, _images[i], _memory[i], 0), "vkBindImageMemory(filter stack)");
        }

        _device = device;
        _extent = extent;
    }

    private void DropIntermediates()
    {
        if (_device is not { } device)
        {
            return;
        }

        _ = device.Api.DeviceWaitIdle(device.Device);
        for (var i = 0; i < 2; i++)
        {
            device.Api.DestroyImage(device.Device, _images[i], null);
            device.Api.FreeMemory(device.Device, _memory[i], null);
            _images[i] = default;
            _memory[i] = default;
        }

        _device = null;
        _extent = default;
    }
}
