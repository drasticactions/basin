using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanFormatTable
{
    private static readonly VulkanFormatEntry[] Entries =
    [
        new(DrmFormat.Xrgb8888, Format.B8G8R8A8Unorm, Format.B8G8R8A8Srgb, IsYcbcr: false),
        new(DrmFormat.Argb8888, Format.B8G8R8A8Unorm, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Xbgr8888, Format.R8G8B8A8Unorm, Format.R8G8B8A8Srgb, IsYcbcr: false),
        new(DrmFormat.Abgr8888, Format.R8G8B8A8Unorm, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Xrgb2101010, Format.A2R10G10B10UnormPack32, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Argb2101010, Format.A2R10G10B10UnormPack32, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Xbgr2101010, Format.A2B10G10R10UnormPack32, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Abgr2101010, Format.A2B10G10R10UnormPack32, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Xbgr16161616f, Format.R16G16B16A16Sfloat, Format.Undefined, IsYcbcr: false, LinearContent: true),
        new(DrmFormat.Abgr16161616f, Format.R16G16B16A16Sfloat, Format.Undefined, IsYcbcr: false, LinearContent: true),
        new(DrmFormat.Rgb565, Format.R5G6B5UnormPack16, Format.Undefined, IsYcbcr: false),
        new(DrmFormat.Nv12, Format.G8B8R82Plane420Unorm, Format.Undefined, IsYcbcr: true),
        new(DrmFormat.P010, Format.G10X6B10X6R10X62Plane420Unorm3Pack16, Format.Undefined, IsYcbcr: true),
    ];

    public const ImageUsageFlags ShmTextureUsage =
        ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit;

    public const ImageUsageFlags DmabufTextureUsage =
        ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit;

    public const ImageUsageFlags RenderUsage = ImageUsageFlags.ColorAttachmentBit;

    private const FormatFeatureFlags ShmTextureFeatures =
        FormatFeatureFlags.TransferSrcBit | FormatFeatureFlags.TransferDstBit |
        FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit;

    private const FormatFeatureFlags DmabufTextureFeatures =
        FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit;

    private const FormatFeatureFlags YcbcrTextureFeatures =
        FormatFeatureFlags.SampledImageYcbcrConversionLinearFilterBit |
        FormatFeatureFlags.MidpointChromaSamplesBit;

    private const FormatFeatureFlags RenderFeatures =
        FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.ColorAttachmentBlendBit;

    private readonly Vk _api;
    private readonly PhysicalDevice _physical;
    private readonly Dictionary<DrmFormat, VulkanFormatProps> _formats = [];

    public DrmFormatSet ShmFormats { get; } = new();

    public DrmFormatSet DmabufTextureFormats { get; } = new();

    public DrmFormatSet DmabufRgbTextureFormats { get; } = new();

    public DrmFormatSet DmabufRenderFormats { get; } = new();

    public VulkanFormatTable(Vk api, PhysicalDevice physical, bool ycbcrSampling)
    {
        _api = api;
        _physical = physical;
        foreach (var entry in Entries)
        {
            if (entry.IsYcbcr && !ycbcrSampling)
            {
                continue;
            }

            Query(entry);
        }
    }

    public bool TryGet(DrmFormat format, out VulkanFormatProps props) =>
        _formats.TryGetValue(format, out props!);

    public bool TryGetModifierPlaneCount(DrmFormat format, ulong modifier, out uint planeCount)
    {
        planeCount = 0;
        if (!_formats.TryGetValue(format, out var props))
        {
            return false;
        }

        if (props.TextureModifiers.TryGetValue(modifier, out var texture))
        {
            planeCount = texture.PlaneCount;
            return true;
        }

        if (props.RenderModifiers.TryGetValue(modifier, out var render))
        {
            planeCount = render.PlaneCount;
            return true;
        }

        return false;
    }

    private void Query(in VulkanFormatEntry entry)
    {
        var modifierList = new DrmFormatModifierPropertiesListEXT
        {
            SType = StructureType.DrmFormatModifierPropertiesListExt,
        };
        var formatProperties = new FormatProperties2
        {
            SType = StructureType.FormatProperties2,
            PNext = &modifierList,
        };
        _api.GetPhysicalDeviceFormatProperties2(_physical, entry.Vk, &formatProperties);

        var props = new VulkanFormatProps { Entry = entry };
        var supported = false;

        if (!entry.IsYcbcr &&
            (formatProperties.FormatProperties.OptimalTilingFeatures & ShmTextureFeatures) == ShmTextureFeatures)
        {
            supported |= QueryShm(entry, props);
        }

        if (modifierList.DrmFormatModifierCount > 0)
        {
            var modifiers = new DrmFormatModifierPropertiesEXT[modifierList.DrmFormatModifierCount];
            fixed (DrmFormatModifierPropertiesEXT* modifiersPtr = modifiers)
            {
                modifierList.PDrmFormatModifierProperties = modifiersPtr;
                _api.GetPhysicalDeviceFormatProperties2(_physical, entry.Vk, &formatProperties);
            }

            foreach (var modifier in modifiers)
            {
                supported |= QueryModifier(entry, props, modifier);
            }
        }

        if (supported)
        {
            _formats[entry.Drm] = props;
        }
    }

    private bool QueryShm(in VulkanFormatEntry entry, VulkanFormatProps props)
    {
        var hasMutableSrgb = entry.HasSrgb;
        if (!ProbeImage(entry, ImageTiling.Optimal, ShmTextureUsage, 0, hasMutableSrgb, out var maxWidth, out var maxHeight))
        {
            hasMutableSrgb = false;
            if (!entry.HasSrgb || !ProbeImage(entry, ImageTiling.Optimal, ShmTextureUsage, 0, false, out maxWidth, out maxHeight))
            {
                return false;
            }
        }

        props.ShmSupported = true;
        props.ShmHasMutableSrgb = hasMutableSrgb;
        props.ShmMaxWidth = maxWidth;
        props.ShmMaxHeight = maxHeight;
        ShmFormats.Add(entry.Drm, DrmFormatSet.ModifierLinear);
        return true;
    }

    private bool QueryModifier(in VulkanFormatEntry entry, VulkanFormatProps props, in DrmFormatModifierPropertiesEXT modifier)
    {
        var features = modifier.DrmFormatModifierTilingFeatures;
        var any = false;

        var textureFeatures = entry.IsYcbcr ? DmabufTextureFeatures | YcbcrTextureFeatures : DmabufTextureFeatures;
        if ((features & textureFeatures) == textureFeatures &&
            TryProbeModifier(entry, modifier, DmabufTextureUsage, out var textureProps))
        {
            props.TextureModifiers[modifier.DrmFormatModifier] = textureProps;
            DmabufTextureFormats.Add(entry.Drm, modifier.DrmFormatModifier);
            if (!entry.IsYcbcr)
            {
                DmabufRgbTextureFormats.Add(entry.Drm, modifier.DrmFormatModifier);
            }

            any = true;
        }

        if (!entry.IsYcbcr &&
            modifier.DrmFormatModifierPlaneCount == 1 &&
            (features & RenderFeatures) == RenderFeatures &&
            TryProbeModifier(entry, modifier, RenderUsage, out var renderProps))
        {
            props.RenderModifiers[modifier.DrmFormatModifier] = renderProps;
            DmabufRenderFormats.Add(entry.Drm, modifier.DrmFormatModifier);
            any = true;
        }

        return any;
    }

    private bool TryProbeModifier(
        in VulkanFormatEntry entry,
        in DrmFormatModifierPropertiesEXT modifier,
        ImageUsageFlags usage,
        out VulkanModifierProps result)
    {
        var hasMutableSrgb = entry.HasSrgb;
        if (!ProbeImage(entry, ImageTiling.DrmFormatModifierExt, usage, modifier.DrmFormatModifier, hasMutableSrgb, out var maxWidth, out var maxHeight))
        {
            hasMutableSrgb = false;
            if (!entry.HasSrgb ||
                !ProbeImage(entry, ImageTiling.DrmFormatModifierExt, usage, modifier.DrmFormatModifier, false, out maxWidth, out maxHeight))
            {
                result = default;
                return false;
            }
        }

        result = new VulkanModifierProps(
            modifier.DrmFormatModifierPlaneCount,
            maxWidth,
            maxHeight,
            hasMutableSrgb,
            (modifier.DrmFormatModifierTilingFeatures & FormatFeatureFlags.DisjointBit) != 0);
        return true;
    }

    private bool ProbeImage(
        in VulkanFormatEntry entry,
        ImageTiling tiling,
        ImageUsageFlags usage,
        ulong modifier,
        bool mutableSrgb,
        out uint maxWidth,
        out uint maxHeight)
    {
        maxWidth = 0;
        maxHeight = 0;

        var viewFormats = stackalloc Format[2] { entry.Vk, entry.VkSrgb };
        var formatList = new ImageFormatListCreateInfo
        {
            SType = StructureType.ImageFormatListCreateInfo,
            ViewFormatCount = 2,
            PViewFormats = viewFormats,
        };

        var modifierInfo = new PhysicalDeviceImageDrmFormatModifierInfoEXT
        {
            SType = StructureType.PhysicalDeviceImageDrmFormatModifierInfoExt,
            DrmFormatModifier = modifier,
            SharingMode = SharingMode.Exclusive,
            PNext = mutableSrgb ? &formatList : null,
        };
        var externalInfo = new PhysicalDeviceExternalImageFormatInfo
        {
            SType = StructureType.PhysicalDeviceExternalImageFormatInfo,
            HandleType = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
            PNext = &modifierInfo,
        };

        var isDmabuf = tiling == ImageTiling.DrmFormatModifierExt;
        var info = new PhysicalDeviceImageFormatInfo2
        {
            SType = StructureType.PhysicalDeviceImageFormatInfo2,
            PNext = isDmabuf ? &externalInfo : (mutableSrgb ? &formatList : null),
            Format = entry.Vk,
            Type = ImageType.Type2D,
            Tiling = tiling,
            Usage = usage,
            Flags = mutableSrgb ? ImageCreateFlags.CreateMutableFormatBit : 0,
        };

        var externalProperties = new ExternalImageFormatProperties { SType = StructureType.ExternalImageFormatProperties };
        var properties = new ImageFormatProperties2
        {
            SType = StructureType.ImageFormatProperties2,
            PNext = isDmabuf ? &externalProperties : null,
        };
        if (_api.GetPhysicalDeviceImageFormatProperties2(_physical, &info, &properties) != Result.Success)
        {
            return false;
        }

        if (isDmabuf &&
            (externalProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.ImportableBit) == 0)
        {
            return false;
        }

        maxWidth = properties.ImageFormatProperties.MaxExtent.Width;
        maxHeight = properties.ImageFormatProperties.MaxExtent.Height;
        return true;
    }
}
