using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public sealed unsafe class BlitConversion : ICrossDeviceConversion
{
    private readonly VulkanDevice _device;
    private readonly ImportedDmabuf _sourceImported;
    private readonly Image _destination;
    private readonly DeviceMemory _destinationMemory;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    internal BlitConversion(VulkanDevice device, in DmabufAttributes attributes)
    {
        _device = device;
        _width = attributes.Width;
        _height = attributes.Height;
        var vk = device.Api;

        var exportFd = -1;
        ImportedDmabuf? sourceImported = null;
        try
        {
            sourceImported = VulkanDmabufImport.Import(
                device, attributes, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit);
            _sourceImported = sourceImported;

            (_destination, _destinationMemory, exportFd, var layout) = CreateExportableLinear(device, attributes);

            var source = _sourceImported.Image;
            var destination = _destination;
            device.SubmitImmediate(commands =>
            {
                device.AcquireImported(commands, source);
                device.TransitionToGeneral(commands, destination);
            });

            var converted = new DmabufAttributes
            {
                Width = attributes.Width,
                Height = attributes.Height,
                Format = attributes.Format,
                Modifier = DrmFormatSet.ModifierLinear,
                PlaneCount = 1,
            };
            converted.Fds[0] = exportFd;
            converted.Offsets[0] = (uint)layout.Offset;
            converted.Strides[0] = (uint)layout.RowPitch;
            Buffer = new DmabufBuffer(converted);
            exportFd = -1;

            Refresh();
        }
        catch
        {
            var vkDevice = device.Device;
            sourceImported?.Destroy(device);
            if (_destination.Handle != 0)
            {
                vk.DestroyImage(vkDevice, _destination, null);
            }

            if (_destinationMemory.Handle != 0)
            {
                vk.FreeMemory(vkDevice, _destinationMemory, null);
            }

            if (exportFd >= 0)
            {
                Libc.Close(exportFd);
            }

            throw;
        }
    }

    public IBuffer Buffer { get; }

    public void Refresh()
    {
        var vk = _device.Api;
        var region = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
        };
        region.SrcOffsets.Element1 = new Offset3D(_width, _height, 1);
        region.DstOffsets.Element1 = new Offset3D(_width, _height, 1);

        var source = _sourceImported.Image;
        var destination = _destination;
        _device.SubmitImmediate(commands =>
        {
            var blit = region;
            var acquire = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.MemoryWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
            };
            vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.TransferBit, 0, 1, &acquire, 0, null, 0, null);
            vk.CmdBlitImage(
                commands, source, ImageLayout.General, destination, ImageLayout.General, 1, &blit, Filter.Nearest);
            var release = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit,
            };
            vk.CmdPipelineBarrier(
                commands, PipelineStageFlags.TransferBit, PipelineStageFlags.AllCommandsBit, 0, 1, &release, 0, null, 0, null);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ((DmabufBuffer)Buffer).Destroy();
        var vk = _device.Api;
        _ = vk.DeviceWaitIdle(_device.Device);
        _sourceImported.Destroy(_device);
        vk.DestroyImage(_device.Device, _destination, null);
        vk.FreeMemory(_device.Device, _destinationMemory, null);
    }

    private static uint ExportableMemoryTypeFor(VulkanDevice device, uint typeBits)
    {
        if (device.TryMemoryTypeFor(
            typeBits, MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit, out var shared))
        {
            return shared;
        }

        if (device.TryMemoryTypeFor(typeBits, MemoryPropertyFlags.HostVisibleBit, out var hostVisible))
        {
            return hostVisible;
        }

        return device.TryMemoryTypeFor(typeBits, MemoryPropertyFlags.DeviceLocalBit, out var deviceLocal)
            ? deviceLocal
            : device.MemoryTypeFor(typeBits, 0);
    }

    private static (Image Image, DeviceMemory Memory, int Fd, SubresourceLayout Layout) CreateExportableLinear(
        VulkanDevice device, in DmabufAttributes attributes)
    {
        var vk = device.Api;
        var image = default(Image);
        var memory = default(DeviceMemory);
        try
        {
            var linearModifier = stackalloc ulong[1] { DrmFormatSet.ModifierLinear };
            var modifierList = new ImageDrmFormatModifierListCreateInfoEXT
            {
                SType = StructureType.ImageDrmFormatModifierListCreateInfoExt,
                DrmFormatModifierCount = 1,
                PDrmFormatModifiers = linearModifier,
            };
            var externalInfo = new ExternalMemoryImageCreateInfo
            {
                SType = StructureType.ExternalMemoryImageCreateInfo,
                PNext = &modifierList,
                HandleTypes = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
            };
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                PNext = &externalInfo,
                ImageType = ImageType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                Extent = new Extent3D((uint)attributes.Width, (uint)attributes.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.DrmFormatModifierExt,
                Usage = ImageUsageFlags.TransferDstBit,
                InitialLayout = ImageLayout.Undefined,
            };
            VulkanDevice.Check(vk.CreateImage(device.Device, in imageInfo, null, out image), "vkCreateImage(export)");

            vk.GetImageMemoryRequirements(device.Device, image, out var requirements);
            var dedicated = new MemoryDedicatedAllocateInfo
            {
                SType = StructureType.MemoryDedicatedAllocateInfo,
                Image = image,
            };
            var export = new ExportMemoryAllocateInfo
            {
                SType = StructureType.ExportMemoryAllocateInfo,
                PNext = &dedicated,
                HandleTypes = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
            };
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                PNext = &export,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = ExportableMemoryTypeFor(device, requirements.MemoryTypeBits),
            };
            VulkanDevice.Check(vk.AllocateMemory(device.Device, in allocateInfo, null, out memory), "vkAllocateMemory(export)");
            VulkanDevice.Check(vk.BindImageMemory(device.Device, image, memory, 0), "vkBindImageMemory(export)");

            var getFd = new MemoryGetFdInfoKHR
            {
                SType = StructureType.MemoryGetFDInfoKhr,
                Memory = memory,
                HandleType = ExternalMemoryHandleTypeFlags.DmaBufBitExt,
            };
            int fd;
            VulkanDevice.Check(device.ExternalMemoryFd.GetMemoryF(device.Device, in getFd, &fd), "vkGetMemoryFdKHR");

            var subresource = new ImageSubresource(ImageAspectFlags.MemoryPlane0BitExt, 0, 0);
            vk.GetImageSubresourceLayout(device.Device, image, in subresource, out var layout);
            return (image, memory, fd, layout);
        }
        catch
        {
            if (memory.Handle != 0)
            {
                vk.FreeMemory(device.Device, memory, null);
            }

            if (image.Handle != 0)
            {
                vk.DestroyImage(device.Device, image, null);
            }

            throw;
        }
    }
}
