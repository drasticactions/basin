using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal static unsafe class VulkanDmabufImport
{
    public static ImportedDmabuf Import(VulkanDevice device, in DmabufAttributes attributes, ImageUsageFlags usage)
    {
        var vk = device.Api;

        if (!device.FormatTable.TryGet(attributes.Format, out var formatProps))
        {
            throw new InvalidOperationException($"{attributes.Format} is not importable on this device");
        }

        var forRender = (usage & ImageUsageFlags.ColorAttachmentBit) != 0;
        var modifiers = forRender ? formatProps.RenderModifiers : formatProps.TextureModifiers;

        if (attributes.Modifier == DrmFormatSet.ModifierInvalid)
        {
            throw new InvalidOperationException("implicit-modifier dmabuf; Vulkan imports need an explicit modifier");
        }

        if (!modifiers.TryGetValue(attributes.Modifier, out var modifierProps))
        {
            throw new InvalidOperationException(
                $"modifier 0x{attributes.Modifier:X} is not {(forRender ? "renderable" : "sampleable")} for {attributes.Format} here");
        }

        if (modifierProps.PlaneCount != (uint)attributes.PlaneCount)
        {
            throw new InvalidOperationException(
                $"modifier 0x{attributes.Modifier:X} has {modifierProps.PlaneCount} memory planes on this device, not {attributes.PlaneCount}");
        }

        if ((uint)attributes.Width > modifierProps.MaxWidth || (uint)attributes.Height > modifierProps.MaxHeight)
        {
            throw new InvalidOperationException(
                $"{attributes.Width}x{attributes.Height} exceeds the device's {modifierProps.MaxWidth}x{modifierProps.MaxHeight} for this modifier");
        }

        var disjoint = IsDisjoint(attributes);
        if (disjoint && !modifierProps.SupportsDisjoint)
        {
            throw new InvalidOperationException(
                $"modifier 0x{attributes.Modifier:X} arrived as separate allocations but the device cannot bind it disjointly");
        }

        var result = new ImportedDmabuf
        {
            Width = attributes.Width,
            Height = attributes.Height,
            HasAlpha = attributes.Format.HasAlpha(),
            UsingMutableSrgb = modifierProps.HasMutableSrgb,
        };
        var ownedFd = -1;
        try
        {
            CreateImage(device, attributes, usage, formatProps.Entry, modifierProps.HasMutableSrgb, disjoint, result);
            ImportAndBindMemory(device, attributes, disjoint, result, ref ownedFd);

            if (!formatProps.Entry.IsYcbcr)
            {
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = result.Image,
                    ViewType = ImageViewType.Type2D,
                    Format = formatProps.Entry.Vk,
                    SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                };
                VulkanDevice.Check(vk.CreateImageView(device.Device, in viewInfo, null, out result.View), "vkCreateImageView(dmabuf)");
            }

            return result;
        }
        catch
        {
            if (ownedFd >= 0)
            {
                Libc.Close(ownedFd);
            }

            result.Destroy(device);
            throw;
        }
    }

    private static void CreateImage(
        VulkanDevice device,
        in DmabufAttributes attributes,
        ImageUsageFlags usage,
        in VulkanFormatEntry entry,
        bool mutableSrgb,
        bool disjoint,
        ImportedDmabuf result)
    {
        var planes = stackalloc SubresourceLayout[4];
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            planes[plane] = new SubresourceLayout
            {
                Offset = attributes.Offsets[plane],
                RowPitch = attributes.Strides[plane],
            };
        }

        var viewFormats = stackalloc Format[2] { entry.Vk, entry.VkSrgb };
        var formatList = new ImageFormatListCreateInfo
        {
            SType = StructureType.ImageFormatListCreateInfo,
            ViewFormatCount = 2,
            PViewFormats = viewFormats,
        };
        var modifierInfo = new ImageDrmFormatModifierExplicitCreateInfoEXT
        {
            SType = StructureType.ImageDrmFormatModifierExplicitCreateInfoExt,
            PNext = mutableSrgb ? &formatList : null,
            DrmFormatModifier = attributes.Modifier,
            DrmFormatModifierPlaneCount = (uint)attributes.PlaneCount,
            PPlaneLayouts = planes,
        };
        var externalInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            PNext = &modifierInfo,
            HandleTypes = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
        };
        var flags = disjoint ? ImageCreateFlags.CreateDisjointBit : 0;
        if (mutableSrgb)
        {
            flags |= ImageCreateFlags.CreateMutableFormatBit;
        }

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalInfo,
            Flags = flags,
            ImageType = ImageType.Type2D,
            Format = entry.Vk,
            Extent = new Extent3D((uint)attributes.Width, (uint)attributes.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.DrmFormatModifierExt,
            Usage = usage,
            InitialLayout = ImageLayout.Undefined,
        };
        VulkanDevice.Check(device.Api.CreateImage(device.Device, in imageInfo, null, out result.Image), "vkCreateImage(dmabuf)");
    }

    private static void ImportAndBindMemory(
        VulkanDevice device,
        in DmabufAttributes attributes,
        bool disjoint,
        ImportedDmabuf result,
        ref int ownedFd)
    {
        var vk = device.Api;
        var memoryCount = disjoint ? attributes.PlaneCount : 1;
        result.Memories = new DeviceMemory[memoryCount];

        var binds = stackalloc BindImageMemoryInfo[4];
        var planeBinds = stackalloc BindImagePlaneMemoryInfo[4];

        for (var index = 0; index < memoryCount; index++)
        {
            var fd = Libc.Dup(attributes.Fds[index]);
            ownedFd = fd;
            var fdProperties = new MemoryFdPropertiesKHR { SType = StructureType.MemoryFDPropertiesKhr };
            VulkanDevice.Check(
                device.ExternalMemoryFd.GetMemoryFdProperties(device.Device, ExternalMemoryHandleTypeFlags.DmaBufBitExt, fd, &fdProperties),
                "vkGetMemoryFdPropertiesKHR");

            MemoryRequirements requirements;
            if (disjoint)
            {
                var planeInfo = new ImagePlaneMemoryRequirementsInfo
                {
                    SType = StructureType.ImagePlaneMemoryRequirementsInfo,
                    PlaneAspect = MemoryPlaneAspect(index),
                };
                var requirementsInfo = new ImageMemoryRequirementsInfo2
                {
                    SType = StructureType.ImageMemoryRequirementsInfo2,
                    PNext = &planeInfo,
                    Image = result.Image,
                };
                var requirements2 = new MemoryRequirements2 { SType = StructureType.MemoryRequirements2 };
                vk.GetImageMemoryRequirements2(device.Device, &requirementsInfo, &requirements2);
                requirements = requirements2.MemoryRequirements;
            }
            else
            {
                vk.GetImageMemoryRequirements(device.Device, result.Image, out requirements);
            }

            var dedicated = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                Image = result.Image,
            };
            var import = new ImportMemoryFdInfoKHR
            {
                SType = StructureType.ImportMemoryFDInfoKhr,
                PNext = &dedicated,
                HandleType = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
                Fd = fd,
            };
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &import,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = device.MemoryTypeFor(requirements.MemoryTypeBits & fdProperties.MemoryTypeBits, 0),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out result.Memories[index]), "vkAllocateMemory(dmabuf)");
            ownedFd = -1;

            if (disjoint)
            {
                planeBinds[index] = new BindImagePlaneMemoryInfo
                {
                    SType = StructureType.BindImagePlaneMemoryInfo,
                    PlaneAspect = MemoryPlaneAspect(index),
                };
                binds[index] = new BindImageMemoryInfo
                {
                    SType = StructureType.BindImageMemoryInfo,
                    PNext = &planeBinds[index],
                    Image = result.Image,
                    Memory = result.Memories[index],
                    MemoryOffset = 0,
                };
            }
        }

        if (disjoint)
        {
            VulkanDevice.Check(vk.BindImageMemory2(device.Device, (uint)memoryCount, binds), "vkBindImageMemory2(disjoint)");
        }
        else
        {
            VulkanDevice.Check(vk.BindImageMemory(device.Device, result.Image, result.Memories[0], 0), "vkBindImageMemory(dmabuf)");
        }
    }

    private static ImageAspectFlags MemoryPlaneAspect(int plane) => plane switch
    {
        0 => ImageAspectFlags.MemoryPlane0BitExt,
        1 => ImageAspectFlags.MemoryPlane1BitExt,
        2 => ImageAspectFlags.MemoryPlane2BitExt,
        _ => ImageAspectFlags.MemoryPlane3BitExt,
    };

    private static bool IsDisjoint(in DmabufAttributes attributes)
    {
        if (attributes.PlaneCount <= 1)
        {
            return false;
        }

        if (!Libc.TryInodeOf(attributes.Fds[0], out var first))
        {
            return true;
        }

        for (var plane = 1; plane < attributes.PlaneCount; plane++)
        {
            if (!Libc.TryInodeOf(attributes.Fds[plane], out var other) || first != other)
            {
                return true;
            }
        }

        return false;
    }
}
