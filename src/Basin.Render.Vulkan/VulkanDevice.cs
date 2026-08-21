using Mesa.Gbm;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Basin.Render.Vulkan;

public sealed unsafe class VulkanDevice : IDisposable, IRenderDevice
{
    private readonly VulkanInstance _instance;
    private readonly int _nodeFd;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;

    public readonly Vk Api;

    public readonly Instance Instance;

    public readonly PhysicalDevice Physical;

    public readonly Device Device;

    public readonly Queue Queue;

    public readonly uint QueueFamily;

    public readonly string[] EnabledExtensions;

    public readonly KhrExternalMemoryFd ExternalMemoryFd;

    public readonly KhrSynchronization2 Synchronization2;

    public readonly bool YcbcrSampling;

    internal readonly VulkanCommandPool Ring;

    public VulkanStagingPool Staging { get; private set; } = null!;

    public VulkanDevice(string drmNodePath, string[]? optionalExtensions = null)
    {
        DevicePath = drmNodePath;
        _nodeFd = Libc.Open(drmNodePath, Libc.ORdwr);
        _instance = new VulkanInstance();
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
            throw new InvalidOperationException($"{drmNodePath} has no timeline semaphores");
        }

        if (!sync2Query.Synchronization2)
        {
            throw new InvalidOperationException($"{drmNodePath} has no synchronization2");
        }

        YcbcrSampling = ycbcrQuery.SamplerYcbcrConversion;
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
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &vulkan12Enable,
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
            throw new InvalidOperationException("VK_KHR_external_memory_fd has no entry points");
        }

        if (!Api.TryGetDeviceExtension(Instance, Device, out Synchronization2))
        {
            throw new InvalidOperationException("VK_KHR_synchronization2 has no entry points");
        }

        Ring = new VulkanCommandPool(this);
        Staging = new VulkanStagingPool(this);
    }

    public int DrmFd => _nodeFd;

    public string DevicePath { get; }

    internal readonly VulkanFormatTable FormatTable;

    public DrmFormatSet SampleableFormats => FormatTable.DmabufTextureFormats;

    public DrmFormatSet SampleableRgbFormats => FormatTable.DmabufRgbTextureFormats;

    public DrmFormatSet ShmFormats => FormatTable.ShmFormats;

    public bool TryGetModifierPlaneCount(DrmFormat format, ulong modifier, out uint planeCount) =>
        FormatTable.TryGetModifierPlaneCount(format, modifier, out planeCount);

    public DrmFormatSet RenderableFormats => FormatTable.DmabufRenderFormats;

    private GbmDevice? _gbm;

    public GbmDevice Gbm => _gbm ??= GbmDevice.Create(_nodeFd);

    public Basin.Render.Gbm.GbmAllocator CreateAllocator(Basin.Diagnostics.FdLedger? ledger = null) =>
        new(Gbm, RenderableFormats, ledger);

    public uint MemoryTypeFor(uint typeBits, MemoryPropertyFlags properties)
    {
        return TryMemoryTypeFor(typeBits, properties, out var index)
            ? index
            : throw new InvalidOperationException("no suitable memory type");
    }

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

    public uint ReadbackMemoryTypeFor(uint typeBits)
    {
        return TryMemoryTypeFor(
            typeBits,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
            out var cached)
            ? cached
            : MemoryTypeFor(typeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    public int ExportQueueFence()
    {
        var commands = Ring.Acquire();
        _ = Ring.Submit(commands);
        return Ring.ExportSyncFile(commands);
    }

    public bool PublishWriteFence(in DmabufAttributes attributes) => PublishWriteFence(attributes, null);

    public bool PublishWriteFence(in DmabufAttributes attributes, VulkanDeviceImage? release)
    {
        var commands = Ring.Acquire();
        release?.RecordForeignRelease(commands);
        _ = Ring.Submit(commands);
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

    public ulong SubmitImmediate(Action<CommandBuffer> record)
    {
        var commands = Ring.Acquire();
        record(commands);
        var point = Ring.Submit(commands);
        Ring.Wait(point);
        return point;
    }

    public ulong SubmitImmediate<TState>(TState state, Action<TState, CommandBuffer> record)
    {
        var commands = Ring.Acquire();
        record(state, commands);
        var point = Ring.Submit(commands);
        Ring.Wait(point);
        return point;
    }

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

    public VulkanFenceWait FenceWait => _fenceWait ??= new VulkanFenceWait(this);

    public bool WaitsOnGpu => FenceWait.IsGpuSide;

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

    public static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{what} failed: {result}");
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

        throw new InvalidOperationException($"no Vulkan device matches {drmNodePath}");
    }

    private static PhysicalDevice RequireVulkan12(PhysicalDevice candidate, in PhysicalDeviceProperties properties, string drmNodePath)
    {
        return properties.ApiVersion >= Vk.Version12
            ? candidate
            : throw new InvalidOperationException(
                $"{drmNodePath} is Vulkan {properties.ApiVersion >> 22}.{(properties.ApiVersion >> 12) & 0x3FF}; 1.2 is the floor");
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

        throw new InvalidOperationException("no graphics queue family");
    }
}
