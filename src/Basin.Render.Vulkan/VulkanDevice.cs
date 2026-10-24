using Mesa.Gbm;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Basin.Render.Vulkan;

/// <summary>
/// Owns a Vulkan instance, the logical device on one DRM node, its one queue and its command buffer ring. Every call must come from the compositor thread.
/// </summary>
public sealed unsafe class VulkanDevice : IDisposable, IRenderDevice
{
    private readonly VulkanInstance _instance;
    private readonly int _nodeFd;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;

    /// <summary>
    /// The Vulkan entry points that the instance loaded.
    /// </summary>
    public readonly Vk Api;

    /// <summary>
    /// The Vulkan instance that the device owns.
    /// </summary>
    public readonly Instance Instance;

    /// <summary>
    /// The physical device whose DRM node matches the path that the constructor took.
    /// </summary>
    public readonly PhysicalDevice Physical;

    /// <summary>
    /// The logical device. It is destroyed by <see cref="Dispose"/>.
    /// </summary>
    public readonly Device Device;

    /// <summary>
    /// The one graphics queue that every submission of the device uses.
    /// </summary>
    public readonly Queue Queue;

    /// <summary>
    /// The index of the queue family of <see cref="Queue"/>.
    /// </summary>
    public readonly uint QueueFamily;

    /// <summary>
    /// The device extensions that the device enabled: the required set plus each optional extension that the device offers.
    /// </summary>
    public readonly string[] EnabledExtensions;

    /// <summary>
    /// The entry points of VK_KHR_external_memory_fd.
    /// </summary>
    public readonly KhrExternalMemoryFd ExternalMemoryFd;

    /// <summary>
    /// The entry points of VK_KHR_synchronization2.
    /// </summary>
    public readonly KhrSynchronization2 Synchronization2;

    /// <summary>
    /// A value indicating whether the device samples YCbCr images through a sampler conversion.
    /// </summary>
    public readonly bool YcbcrSampling;

    internal readonly VulkanCommandPool Ring;

    /// <summary>
    /// Gets the pool of host-visible buffers that uploads copy through.
    /// </summary>
    public VulkanStagingPool Staging { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanDevice"/> class. It throws <see cref="InvalidOperationException"/> when no Vulkan device matches the node or a required feature is absent.
    /// </summary>
    /// <param name="drmNodePath">The path of the DRM node, for example a render node.</param>
    /// <param name="optionalExtensions">The device extensions to enable when the device offers them, or <see langword="null"/>.</param>
    /// <param name="instanceExtensions">The extra instance extensions to enable, or <see langword="null"/>.</param>
    public VulkanDevice(string drmNodePath, string[]? optionalExtensions = null, string[]? instanceExtensions = null)
    {
        DevicePath = drmNodePath;
        _nodeFd = Libc.Open(drmNodePath, Libc.ORdwr);
        _instance = new VulkanInstance(instanceExtensions);
        Api = _instance.Api;
        Instance = _instance.Instance;

        Physical = PickDeviceFor(drmNodePath);
        Api.GetPhysicalDeviceMemoryProperties(Physical, out _memoryProperties);

        QueueFamily = FindGraphicsQueueFamily();
        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        string[] required =
        [
            "VK_EXT_image_drm_format_modifier",
            "VK_KHR_image_format_list",
            "VK_EXT_external_memory_dma_buf",
            "VK_KHR_external_memory_fd",
            "VK_EXT_queue_family_foreign",
            "VK_KHR_synchronization2",
        ];

        var globalPriority = OffersExtension("VK_KHR_global_priority");
        if (globalPriority)
        {
            required = [.. required, "VK_KHR_global_priority"];
        }

        EnabledExtensions = optionalExtensions is { Length: > 0 }
            ? [.. required, .. optionalExtensions.Where(e => !required.Contains(e) && OffersExtension(e))]
            : required;

        var ycbcrQuery = new PhysicalDeviceSamplerYcbcrConversionFeatures
        {
            SType = StructureType.PhysicalDeviceSamplerYcbcrConversionFeatures,
        };
        var sync2Query = new PhysicalDeviceSynchronization2FeaturesKHR
        {
            SType = StructureType.PhysicalDeviceSynchronization2FeaturesKhr,
            PNext = &ycbcrQuery,
        };
        var vulkan12Query = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &sync2Query,
        };
        var featuresQuery = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &vulkan12Query,
        };
        Api.GetPhysicalDeviceFeatures2(Physical, &featuresQuery);
        if (!vulkan12Query.TimelineSemaphore)
        {
            throw new InvalidOperationException($"The Vulkan device at {drmNodePath} has no timeline semaphores.");
        }

        if (!sync2Query.Synchronization2)
        {
            throw new InvalidOperationException($"The Vulkan device at {drmNodePath} has no synchronization2.");
        }

        YcbcrSampling = ycbcrQuery.SamplerYcbcrConversion;
        SamplerAnisotropy = featuresQuery.Features.SamplerAnisotropy;
        FormatTable = new VulkanFormatTable(Api, Physical, YcbcrSampling);

        var ycbcrEnable = new PhysicalDeviceSamplerYcbcrConversionFeatures
        {
            SType = StructureType.PhysicalDeviceSamplerYcbcrConversionFeatures,
            SamplerYcbcrConversion = YcbcrSampling,
        };
        var sync2Enable = new PhysicalDeviceSynchronization2FeaturesKHR
        {
            SType = StructureType.PhysicalDeviceSynchronization2FeaturesKhr,
            PNext = &ycbcrEnable,
            Synchronization2 = true,
        };
        var vulkan12Enable = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &sync2Enable,
            TimelineSemaphore = true,
        };

        var queuePriority = new DeviceQueueGlobalPriorityCreateInfoKHR
        {
            SType = StructureType.DeviceQueueGlobalPriorityCreateInfoKhr,
            GlobalPriority = QueueGlobalPriority.High,
        };
        if (globalPriority)
        {
            queueInfo.PNext = &queuePriority;
        }

        var enabledNames = (byte**)SilkMarshal.StringArrayToPtr(EnabledExtensions);
        var enabledFeatures = new PhysicalDeviceFeatures { SamplerAnisotropy = SamplerAnisotropy };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &vulkan12Enable,
            PEnabledFeatures = &enabledFeatures,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = (uint)EnabledExtensions.Length,
            PpEnabledExtensionNames = enabledNames,
        };
        var created = Api.CreateDevice(Physical, in deviceInfo, null, out Device);
        if (created != Result.Success && globalPriority)
        {
            queueInfo.PNext = null;
            created = Api.CreateDevice(Physical, in deviceInfo, null, out Device);
        }

        Check(created, "vkCreateDevice");
        SilkMarshal.Free((nint)enabledNames);
        Api.GetDeviceQueue(Device, QueueFamily, 0, out Queue);

        if (!Api.TryGetDeviceExtension(Instance, Device, out ExternalMemoryFd))
        {
            throw new InvalidOperationException("The driver has no entry points for VK_KHR_external_memory_fd.");
        }

        if (!Api.TryGetDeviceExtension(Instance, Device, out Synchronization2))
        {
            throw new InvalidOperationException("The driver has no entry points for VK_KHR_synchronization2.");
        }

        Ring = new VulkanCommandPool(this);
        Staging = new VulkanStagingPool(this);
    }

    /// <inheritdoc/>
    public int DrmFd => _nodeFd;

    /// <summary>
    /// Gets the number of command buffers that can be in flight at once. <see cref="BeginCommands"/> waits for the oldest one when all are in use.
    /// </summary>
    public int FramesInFlight => VulkanCommandPool.Slots;

    /// <inheritdoc/>
    public string DevicePath { get; }

    internal readonly VulkanFormatTable FormatTable;

    /// <summary>
    /// Gets the dmabuf formats and modifiers that the device can sample, YCbCr formats included.
    /// </summary>
    public DrmFormatSet SampleableFormats => FormatTable.DmabufTextureFormats;

    /// <summary>
    /// Gets the subset of <see cref="SampleableFormats"/> that has no YCbCr format.
    /// </summary>
    public DrmFormatSet SampleableRgbFormats => FormatTable.DmabufRgbTextureFormats;

    /// <summary>
    /// Gets the formats that a <see cref="VulkanUploadImage"/> can upload from memory, each with the linear modifier.
    /// </summary>
    public DrmFormatSet ShmFormats => FormatTable.ShmFormats;

    /// <summary>
    /// Gets the number of memory planes that the device uses for a format and modifier pair.
    /// </summary>
    /// <param name="format">The DRM format.</param>
    /// <param name="modifier">The DRM format modifier.</param>
    /// <param name="planeCount">The number of memory planes, or 0 on failure.</param>
    /// <returns><see langword="false"/> when the device does not know the pair.</returns>
    public bool TryGetModifierPlaneCount(DrmFormat format, ulong modifier, out uint planeCount) =>
        FormatTable.TryGetModifierPlaneCount(format, modifier, out planeCount);

    /// <summary>
    /// Gets the dmabuf formats and modifiers that the device can render into.
    /// </summary>
    public DrmFormatSet RenderableFormats => FormatTable.DmabufRenderFormats;

    private DrmFormatSet? _bgraRenderableFormats;

    /// <summary>
    /// Gets the ARGB8888 and XRGB8888 entries of <see cref="RenderableFormats"/>. The set is built on first use.
    /// </summary>
    public DrmFormatSet BgraRenderableFormats => _bgraRenderableFormats ??= BgraOnly(RenderableFormats);

    private static DrmFormatSet BgraOnly(DrmFormatSet formats)
    {
        var result = new DrmFormatSet();
        foreach (var format in formats.Formats)
        {
            if (format is not (DrmFormat.Argb8888 or DrmFormat.Xrgb8888))
            {
                continue;
            }

            foreach (var modifier in formats.ModifiersOf(format))
            {
                result.Add(format, modifier);
            }
        }

        return result;
    }

    private GbmDevice? _gbm;

    /// <summary>
    /// Gets a GBM device on the DRM node. It is created on first use and the device disposes it.
    /// </summary>
    public GbmDevice Gbm => _gbm ??= GbmDevice.Create(_nodeFd);

    /// <summary>
    /// Creates an allocator that makes dmabufs in <see cref="RenderableFormats"/> through <see cref="Gbm"/>.
    /// </summary>
    /// <param name="ledger">The ledger that records the fds of each buffer, or <see langword="null"/>.</param>
    /// <returns>The allocator, which the caller disposes before the device.</returns>
    public Basin.Render.Gbm.GbmAllocator CreateAllocator(Basin.Diagnostics.FdLedger? ledger = null) =>
        new(Gbm, RenderableFormats, ledger);

    /// <summary>
    /// Finds the first memory type that a resource accepts and that has every requested property. It throws <see cref="InvalidOperationException"/> when no type matches.
    /// </summary>
    /// <param name="typeBits">The memory type bits from the memory requirements of the resource.</param>
    /// <param name="properties">The properties that the type must have.</param>
    /// <returns>The index of the memory type.</returns>
    public uint MemoryTypeFor(uint typeBits, MemoryPropertyFlags properties)
    {
        return TryMemoryTypeFor(typeBits, properties, out var index)
            ? index
            : throw new InvalidOperationException("The device has no suitable memory type.");
    }

    /// <summary>
    /// Finds the first memory type that a resource accepts and that has every requested property.
    /// </summary>
    /// <param name="typeBits">The memory type bits from the memory requirements of the resource.</param>
    /// <param name="properties">The properties that the type must have.</param>
    /// <param name="index">The index of the memory type, or 0 on failure.</param>
    /// <returns><see langword="false"/> when no type matches.</returns>
    public bool TryMemoryTypeFor(uint typeBits, MemoryPropertyFlags properties, out uint index)
    {
        for (var i = 0; i < _memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeBits & (1u << i)) != 0 &&
                (_memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                index = (uint)i;
                return true;
            }
        }

        index = 0;
        return false;
    }

    /// <summary>
    /// Finds a memory type for CPU reads. It prefers host-cached memory and throws <see cref="InvalidOperationException"/> when no host-visible, coherent type matches.
    /// </summary>
    /// <param name="typeBits">The memory type bits from the memory requirements of the resource.</param>
    /// <returns>The index of the memory type.</returns>
    public uint ReadbackMemoryTypeFor(uint typeBits)
    {
        return TryMemoryTypeFor(
            typeBits,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
            out var cached)
            ? cached
            : MemoryTypeFor(typeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    /// <summary>
    /// Submits an empty command buffer and exports a fence for it. The fence signals when the queue finished all earlier work.
    /// </summary>
    /// <returns>A sync_file fd that the caller owns, or -1 when the device cannot export one.</returns>
    public int ExportQueueFence()
    {
        var commands = Ring.Acquire();
        _ = Ring.Submit(commands, exportable: true);
        return Ring.ExportSyncFile(commands);
    }

    /// <summary>
    /// Submits an empty command buffer and attaches its fence to each plane of a dmabuf as a write fence. Implicit-sync readers then wait for the queue.
    /// </summary>
    /// <param name="attributes">The dmabuf to fence. Its fds stay owned by the caller.</param>
    /// <returns><see langword="false"/> when the export of the fence failed or a plane did not take it.</returns>
    public bool PublishWriteFence(in DmabufAttributes attributes) => PublishWriteFence(attributes, null);

    /// <summary>
    /// Releases an image to the foreign queue family, then submits and attaches the fence to each plane of a dmabuf as a write fence.
    /// </summary>
    /// <param name="attributes">The dmabuf to fence. Its fds stay owned by the caller.</param>
    /// <param name="release">The imported image to release, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the export of the fence failed or a plane did not take it.</returns>
    public bool PublishWriteFence(in DmabufAttributes attributes, VulkanDeviceImage? release) =>
        PublishWriteFence(Ring.Acquire(), attributes, release, out _);

    /// <summary>
    /// Gets a command buffer from the ring in the recording state. It waits for the oldest submission when every buffer is in flight.
    /// </summary>
    /// <returns>The command buffer, which the caller must submit through this device.</returns>
    public CommandBuffer BeginCommands() => Ring.Acquire();

    /// <summary>
    /// Ends and submits a command buffer from <see cref="BeginCommands"/>. It throws <see cref="InvalidOperationException"/> when the submit fails.
    /// </summary>
    /// <param name="commands">The command buffer to submit.</param>
    /// <returns>The timeline point that signals when the commands finish.</returns>
    public ulong Submit(CommandBuffer commands) => Ring.Submit(commands);

    /// <summary>
    /// Reads the timeline of the queue and tells whether a submission finished.
    /// </summary>
    /// <param name="point">A timeline point from a submit call.</param>
    /// <returns><see langword="true"/> when the GPU reached the point.</returns>
    public bool IsComplete(ulong point) => point <= Ring.ReadCompleted();

    /// <summary>
    /// Gets the timeline point that the GPU reached. Each read queries the device.
    /// </summary>
    public ulong CompletedPoint => Ring.ReadCompleted();

    /// <summary>
    /// Gets a value indicating whether the device enabled anisotropic sampling.
    /// </summary>
    public bool SamplerAnisotropy { get; }

    /// <summary>
    /// Ends and submits a command buffer and exports a fence for it.
    /// </summary>
    /// <param name="commands">A command buffer from <see cref="BeginCommands"/>.</param>
    /// <param name="point">The timeline point of the submission.</param>
    /// <returns>A sync_file fd that the caller owns, or -1 when the device cannot export one.</returns>
    public int SubmitExportable(CommandBuffer commands, out ulong point)
    {
        point = Ring.Submit(commands, exportable: true);
        return Ring.ExportSyncFile(commands);
    }

    /// <summary>
    /// Ends and submits a command buffer that first waits on binary semaphores, and exports a fence for it. It throws <see cref="InvalidOperationException"/> when the submit fails.
    /// </summary>
    /// <param name="commands">A command buffer from <see cref="BeginCommands"/>.</param>
    /// <param name="waits">The binary semaphores that the GPU waits on before it runs the commands.</param>
    /// <param name="point">The timeline point of the submission.</param>
    /// <returns>A sync_file fd that the caller owns, or -1 when the device cannot export one.</returns>
    public int SubmitExportable(CommandBuffer commands, ReadOnlySpan<Silk.NET.Vulkan.Semaphore> waits, out ulong point)
    {
        if (!Ring.TrySubmitFrame(default, commands, waits, exportable: true, out point))
        {
            throw new InvalidOperationException("vkQueueSubmit2 failed for a submission with waits.");
        }

        return Ring.ExportSyncFile(commands);
    }

    /// <summary>
    /// Blocks until the GPU reaches a timeline point. It throws <see cref="InvalidOperationException"/> after ten seconds.
    /// </summary>
    /// <param name="point">A timeline point from a submit call.</param>
    public void WaitFor(ulong point) => Ring.Wait(point);

    /// <summary>
    /// Releases an image to the foreign queue family, submits recorded commands, and attaches their fence to each plane of a dmabuf as a write fence.
    /// </summary>
    /// <param name="commands">A command buffer from <see cref="BeginCommands"/>. This call submits it.</param>
    /// <param name="attributes">The dmabuf to fence. Its fds stay owned by the caller.</param>
    /// <param name="release">The imported image to release, or <see langword="null"/>.</param>
    /// <param name="point">The timeline point of the submission.</param>
    /// <returns><see langword="false"/> when the export of the fence failed or a plane did not take it. The commands are submitted in both cases.</returns>
    public bool PublishWriteFence(
        CommandBuffer commands, in DmabufAttributes attributes, VulkanDeviceImage? release, out ulong point)
    {
        release?.RecordForeignRelease(commands);
        point = Ring.Submit(commands, exportable: true);
        var fence = Ring.ExportSyncFile(commands);
        if (fence < 0)
        {
            return false;
        }

        var published = true;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            published &= RenderFences.ImportDmabufSyncFile(attributes.Fds[plane], forWrite: true, fence);
        }

        Libc.Close(fence);
        return published;
    }

    /// <summary>
    /// Records commands through a callback, submits them, and blocks until the GPU finishes them.
    /// </summary>
    /// <param name="record">The callback that records into the command buffer.</param>
    /// <returns>The timeline point of the finished submission.</returns>
    public ulong SubmitImmediate(Action<CommandBuffer> record)
    {
        var commands = Ring.Acquire();
        record(commands);
        var point = Ring.Submit(commands);
        Ring.Wait(point);
        return point;
    }

    /// <summary>
    /// Records commands through a callback with a state value, submits them, and blocks until the GPU finishes them.
    /// </summary>
    /// <typeparam name="TState">The type of the state that the callback reads.</typeparam>
    /// <param name="state">The value to pass to the callback, so that the callback captures nothing.</param>
    /// <param name="record">The callback that records into the command buffer.</param>
    /// <returns>The timeline point of the finished submission.</returns>
    public ulong SubmitImmediate<TState>(TState state, Action<TState, CommandBuffer> record)
    {
        var commands = Ring.Acquire();
        record(state, commands);
        var point = Ring.Submit(commands);
        Ring.Wait(point);
        return point;
    }

    /// <summary>
    /// Records a barrier that moves an imported image from the foreign queue family to the queue of this device, in the general layout.
    /// </summary>
    /// <param name="commands">The command buffer to record into.</param>
    /// <param name="image">An image imported from a dmabuf.</param>
    public void AcquireImported(CommandBuffer commands, Image image)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyForeignExt,
            DstQueueFamilyIndex = QueueFamily,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Api.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>
    /// Records a barrier that gives an imported image back to the foreign queue family, in the general layout.
    /// </summary>
    /// <param name="commands">The command buffer to record into.</param>
    /// <param name="image">An image imported from a dmabuf.</param>
    public void ReleaseImported(CommandBuffer commands, Image image)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = 0,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = QueueFamily,
            DstQueueFamilyIndex = Vk.QueueFamilyForeignExt,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Api.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.BottomOfPipeBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>
    /// Records a barrier that moves an image from the undefined layout to the general layout. The content of the image is discarded.
    /// </summary>
    /// <param name="commands">The command buffer to record into.</param>
    /// <param name="image">The image to transition.</param>
    public void TransitionToGeneral(CommandBuffer commands, Image image)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Api.CmdPipelineBarrier(
            commands,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    private VulkanFenceWait? _fenceWait;

    /// <summary>
    /// Gets the object that makes the queue wait for sync_file fences. It is created on first use and the device disposes it.
    /// </summary>
    public VulkanFenceWait FenceWait => _fenceWait ??= new VulkanFenceWait(this);

    /// <summary>
    /// Gets a value indicating whether <see cref="FenceWait"/> waits on the GPU. When it is <see langword="false"/>, a wait blocks the calling thread.
    /// </summary>
    public bool WaitsOnGpu => FenceWait.IsGpuSide;

    /// <summary>
    /// Occurs before an image view is destroyed, so that a holder of descriptor sets for the view can free them. <see cref="NotifyImageViewDestroying"/> raises it.
    /// </summary>
    public event Action<ImageView>? ImageViewDestroying;

    /// <summary>
    /// Raises <see cref="ImageViewDestroying"/>. The owner of a view that an effect or a renderer can sample must call it before it destroys the view.
    /// </summary>
    /// <param name="view">The view that is about to be destroyed.</param>
    public void NotifyImageViewDestroying(ImageView view) => ImageViewDestroying?.Invoke(view);

    /// <summary>
    /// Waits for the device to go idle, destroys the device, the instance and every pool, and closes the DRM node fd. Dispose every object made on the device first.
    /// </summary>
    public void Dispose()
    {
        _ = Api.DeviceWaitIdle(Device);
        _fenceWait?.Dispose();
        _gbm?.Dispose();
        Ring.Dispose();
        Staging.Dispose();
        Api.DestroyDevice(Device, null);
        _instance.Dispose();
        if (_nodeFd >= 0)
        {
            Libc.Close(_nodeFd);
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with the name of the call when a Vulkan result is not success.
    /// </summary>
    /// <param name="result">The result of the Vulkan call.</param>
    /// <param name="what">The name of the call for the message.</param>
    public static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{what} failed with {result}.");
        }
    }

    private PhysicalDevice PickDeviceFor(string drmNodePath)
    {
        var rdev = Libc.RdevOf(drmNodePath);
        uint count = 0;
        Check(Api.EnumeratePhysicalDevices(Instance, ref count, null), "vkEnumeratePhysicalDevices");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            Check(Api.EnumeratePhysicalDevices(Instance, ref count, devicesPtr), "vkEnumeratePhysicalDevices");
        }

        foreach (var candidate in devices)
        {
            var drm = new PhysicalDeviceDrmPropertiesEXT { SType = StructureType.PhysicalDeviceDrmPropertiesExt };
            var properties = new PhysicalDeviceProperties2
            {
                SType = StructureType.PhysicalDeviceProperties2,
                PNext = &drm,
            };
            Api.GetPhysicalDeviceProperties2(candidate, &properties);
            if ((drm.HasRender.Value != 0 && Matches(rdev, drm.RenderMajor, drm.RenderMinor)) ||
                (drm.HasPrimary.Value != 0 && Matches(rdev, drm.PrimaryMajor, drm.PrimaryMinor)))
            {
                return RequireVulkan12(candidate, in properties.Properties, drmNodePath);
            }
        }

        if (count == 1)
        {
            Api.GetPhysicalDeviceProperties(devices[0], out var properties);
            return RequireVulkan12(devices[0], in properties, drmNodePath);
        }

        throw new InvalidOperationException($"No Vulkan device matches {drmNodePath}.");
    }

    private static PhysicalDevice RequireVulkan12(PhysicalDevice candidate, in PhysicalDeviceProperties properties, string drmNodePath)
    {
        return properties.ApiVersion >= Vk.Version12
            ? candidate
            : throw new InvalidOperationException(
                $"The device at {drmNodePath} supports Vulkan {properties.ApiVersion >> 22}.{(properties.ApiVersion >> 12) & 0x3FF}. The Vulkan renderer needs Vulkan 1.2 or later.");
    }

    private static bool Matches(ulong rdev, long major, long minor) =>
        ((rdev >> 8) & 0xFFF) == (ulong)major && ((rdev & 0xFF) | ((rdev >> 12) & 0xFFF00)) == (ulong)minor;

    private bool OffersExtension(string name)
    {
        uint count = 0;
        Check(Api.EnumerateDeviceExtensionProperties(Physical, (byte*)null, ref count, null), "vkEnumerateDeviceExtensionProperties");
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPtr = properties)
        {
            Check(Api.EnumerateDeviceExtensionProperties(Physical, (byte*)null, ref count, propertiesPtr), "vkEnumerateDeviceExtensionProperties");
        }

        for (var i = 0; i < properties.Length; i++)
        {
            fixed (ExtensionProperties* property = &properties[i])
            {
                if (SilkMarshal.PtrToString((nint)property->ExtensionName) == name)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private uint FindGraphicsQueueFamily()
    {
        uint count = 0;
        Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* familiesPtr = families)
        {
            Api.GetPhysicalDeviceQueueFamilyProperties(Physical, ref count, familiesPtr);
        }

        for (uint i = 0; i < count; i++)
        {
            if ((families[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("The device has no graphics queue family.");
    }
}
