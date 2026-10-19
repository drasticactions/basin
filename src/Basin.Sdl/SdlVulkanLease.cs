using Basin.Render.Skia;
using SDL3;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

public sealed unsafe class SdlVulkanLease : ISdlRenderLease
{
    private const ulong AcquireTimeoutNs = 1_000_000_000UL;

    private readonly SdlVulkanDevice _device;
    private readonly SdlWindow _window;
    private readonly nint _rawSurface;
    private readonly SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Format _format;
    private SKColorType _colorType;
    private Extent2D _extent;
    private Image[] _images = [];
    private GRBackendRenderTarget?[] _targets = [];
    private SKSurface?[] _surfaces = [];
    private long[] _presentedAt = [];
    private long _frame;
    private uint _index;
    private bool _acquired;
    private bool _stale = true;
    private Fence _fence;
    private CommandPool _pool;
    private CommandBuffer _commands;
    private bool _disposed;

    internal SdlVulkanLease(SdlVulkanDevice device, SdlWindow window)
    {
        _device = device;
        _window = window;
        if (!SDL.VulkanCreateSurface(window.Handle, (nint)device.Instance.Handle, 0, out _rawSurface))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed: {SDL.GetError()}");
        }

        _surface = new SurfaceKHR((ulong)_rawSurface);
        var api = device.Api;
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        SdlVulkanDevice.Check(api.CreateFence(device.Device, in fenceInfo, null, out _fence), "vkCreateFence");
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.QueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        SdlVulkanDevice.Check(api.CreateCommandPool(device.Device, in poolInfo, null, out _pool), "vkCreateCommandPool");
        var allocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        SdlVulkanDevice.Check(api.AllocateCommandBuffers(device.Device, in allocate, out _commands), "vkAllocateCommandBuffers");
        var (width, height) = window.PixelSize;
        _extent = new Extent2D((uint)width, (uint)height);
    }

    public string Name => SdlRenderDevices.Vulkan;

    private SdlHdrState _requested = SdlHdrState.Sdr;
    private bool _hdrActive;

    public SdlHdrState Hdr => _hdrActive ? _requested : SdlHdrState.Sdr;

    public bool TrySetHdr(in SdlHdrState state)
    {
        if (state.Enabled != _requested.Enabled)
        {
            _stale = true;
        }

        _requested = state;
        return !state.Enabled || OffersHdr10();
    }

    private bool OffersHdr10()
    {
        foreach (var offered in SurfaceFormats())
        {
            if (offered.ColorSpace == ColorSpaceKHR.SpaceHdr10ST2084Ext && Hdr10ColorType(offered.Format) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static SKColorType? Hdr10ColorType(Format format) => format switch
    {
        Format.A2B10G10R10UnormPack32 => SKColorType.Rgba1010102,
        Format.A2R10G10B10UnormPack32 => SKColorType.Bgra1010102,
        Format.R16G16B16A16Sfloat => SKColorType.RgbaF16,
        _ => null,
    };

    private SurfaceFormatKHR[] SurfaceFormats()
    {
        uint count = 0;
        _ = _device.Surfaces.GetPhysicalDeviceSurfaceFormats(_device.Physical, _surface, ref count, null);
        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* list = formats)
        {
            _ = _device.Surfaces.GetPhysicalDeviceSurfaceFormats(_device.Physical, _surface, ref count, list);
        }

        return formats;
    }

    public Format SurfaceFormat => _format;

    public void Resize(int pixelWidth, int pixelHeight)
    {
        var extent = new Extent2D((uint)Math.Max(1, pixelWidth), (uint)Math.Max(1, pixelHeight));
        if (extent.Width != _extent.Width || extent.Height != _extent.Height)
        {
            _extent = extent;
            _stale = true;
        }
    }

    public bool MakeCurrent() => true;

    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) => false;

    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas = null!;
        age = 0;
        context = _device.Context;
        if (context is null || context.IsAbandoned)
        {
            return false;
        }

        if (_stale && !Recreate())
        {
            return false;
        }

        var api = _device.Api;
        var result = Acquire();
        if (result is Result.ErrorOutOfDateKhr)
        {
            if (!Recreate())
            {
                return false;
            }

            result = Acquire();
        }

        if (result is Result.ErrorDeviceLost)
        {
            context.AbandonContext(false);
            return false;
        }

        if (result is not (Result.Success or Result.SuboptimalKhr))
        {
            Log.Warn($"vkAcquireNextImageKHR returned {result}");
            return false;
        }

        if (result is Result.SuboptimalKhr)
        {
            _stale = true;
        }

        _acquired = true;
        if (_presentedAt[_index] > 0)
        {
            Transition(_images[_index], ImageLayout.PresentSrcKhr, ImageLayout.ColorAttachmentOptimal);
            age = (int)Math.Min(int.MaxValue, _frame - _presentedAt[_index] + 1);
        }

        canvas = SurfaceAt(_index, context).Canvas;
        return true;
    }

    private Result Acquire()
    {
        var api = _device.Api;
        _ = api.ResetFences(_device.Device, 1, in _fence);
        var result = _device.Swapchains.AcquireNextImage(
            _device.Device, _swapchain, AcquireTimeoutNs, default, _fence, ref _index);
        if (result is Result.Success or Result.SuboptimalKhr)
        {
            _ = api.WaitForFences(_device.Device, 1, in _fence, true, AcquireTimeoutNs);
        }

        return result;
    }

    private SKSurface SurfaceAt(uint index, GRContext context)
    {
        if (_surfaces[index] is { } existing)
        {
            return existing;
        }

        var target = SkiaCensus.Track(new GRBackendRenderTarget((int)_extent.Width, (int)_extent.Height, new GRVkImageInfo
        {
            Image = _images[index].Handle,
            ImageTiling = (uint)ImageTiling.Optimal,
            ImageLayout = (uint)ImageLayout.Undefined,
            Format = (uint)_format,
            ImageUsageFlags = (uint)(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit),
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = _device.QueueFamily,
        }));
        var surface = SKSurface.Create(context, target, GRSurfaceOrigin.TopLeft, _colorType)
            ?? throw new InvalidOperationException($"Ganesh rejected swapchain image {index} in {_format}");
        _targets[index] = target;
        _surfaces[index] = SkiaCensus.Track(surface);
        return surface;
    }

    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_acquired || _device.Context is not { } context)
        {
            return;
        }

        _acquired = false;
        var skia = _device.DmabufRenderer as SkiaVulkanRenderer;
        if (OperatingSystem.IsLinux())
        {
            skia?.AcquireForeignImages();
        }

        context.Flush(submit: true, synchronous: true);
        if (OperatingSystem.IsLinux())
        {
            skia?.ReleaseForeignImages();
        }

        Transition(_images[_index], ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
        var swapchain = _swapchain;
        var index = _index;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &index,
        };
        var result = _device.Swapchains.QueuePresent(_device.Queue, in present);
        _presentedAt[_index] = ++_frame;
        if (result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
        {
            _stale = true;
        }
        else if (result is Result.ErrorDeviceLost)
        {
            context.AbandonContext(false);
        }
    }

    private void Transition(Image image, ImageLayout from, ImageLayout to)
    {
        var api = _device.Api;
        _ = api.ResetCommandBuffer(_commands, 0);
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        _ = api.BeginCommandBuffer(_commands, in begin);
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = from == ImageLayout.ColorAttachmentOptimal ? AccessFlags.ColorAttachmentWriteBit : 0,
            DstAccessMask = to == ImageLayout.ColorAttachmentOptimal
                ? AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
                : 0,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        api.CmdPipelineBarrier(
            _commands, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit, 0,
            0, null, 0, null, 1, &barrier);
        _ = api.EndCommandBuffer(_commands);
        var commands = _commands;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commands,
        };
        _ = api.ResetFences(_device.Device, 1, in _fence);
        _ = api.QueueSubmit(_device.Queue, 1, in submit, _fence);
        _ = api.WaitForFences(_device.Device, 1, in _fence, true, AcquireTimeoutNs);
    }

    private bool Recreate()
    {
        var api = _device.Api;
        var surfaces = _device.Surfaces;
        _ = api.DeviceWaitIdle(_device.Device);
        ReleaseImages();
        SdlVulkanDevice.Check(
            surfaces.GetPhysicalDeviceSurfaceCapabilities(_device.Physical, _surface, out var caps),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        var extent = caps.CurrentExtent.Width != uint.MaxValue
            ? caps.CurrentExtent
            : new Extent2D(
                Math.Clamp(_extent.Width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(_extent.Height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height));
        if (extent.Width == 0 || extent.Height == 0)
        {
            return false;
        }

        (_format, _colorType, var colorSpace) = ChooseFormat();
        var count = caps.MinImageCount + 1;
        if (caps.MaxImageCount > 0)
        {
            count = Math.Min(count, caps.MaxImageCount);
        }

        var alpha = (caps.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0
            ? CompositeAlphaFlagsKHR.OpaqueBitKhr
            : CompositeAlphaFlagsKHR.InheritBitKhr;
        var old = _swapchain;
        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = count,
            ImageFormat = _format,
            ImageColorSpace = colorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = alpha,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = false,
            OldSwapchain = old,
        };
        SdlVulkanDevice.Check(_device.Swapchains.CreateSwapchain(_device.Device, in info, null, out _swapchain), "vkCreateSwapchainKHR");
        if (old.Handle != 0)
        {
            _device.Swapchains.DestroySwapchain(_device.Device, old, null);
        }

        uint imageCount = 0;
        _ = _device.Swapchains.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, null);
        _images = new Image[imageCount];
        fixed (Image* images = _images)
        {
            _ = _device.Swapchains.GetSwapchainImages(_device.Device, _swapchain, ref imageCount, images);
        }

        _targets = new GRBackendRenderTarget?[imageCount];
        _surfaces = new SKSurface?[imageCount];
        _presentedAt = new long[imageCount];
        _extent = extent;
        _stale = false;
        return true;
    }

    private (Format Format, SKColorType ColorType, ColorSpaceKHR ColorSpace) ChooseFormat()
    {
        var formats = SurfaceFormats();
        _hdrActive = false;
        if (_requested.Enabled)
        {
            foreach (var wanted in (ReadOnlySpan<Format>)[Format.A2B10G10R10UnormPack32, Format.A2R10G10B10UnormPack32, Format.R16G16B16A16Sfloat])
            {
                foreach (var offered in formats)
                {
                    if (offered.Format == wanted && offered.ColorSpace == ColorSpaceKHR.SpaceHdr10ST2084Ext)
                    {
                        _hdrActive = true;
                        return (wanted, Hdr10ColorType(wanted)!.Value, offered.ColorSpace);
                    }
                }
            }
        }

        foreach (var wanted in (ReadOnlySpan<Format>)[Format.B8G8R8A8Unorm, Format.R8G8B8A8Unorm])
        {
            foreach (var offered in formats)
            {
                if (offered.Format == wanted && offered.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return (wanted, wanted == Format.B8G8R8A8Unorm ? SKColorType.Bgra8888 : SKColorType.Rgba8888, offered.ColorSpace);
                }
            }
        }

        throw new InvalidOperationException("the SDL window's Vulkan surface offers no 8-bit sRGB format");
    }

    private void ReleaseImages()
    {
        for (var i = 0; i < _surfaces.Length; i++)
        {
            if (_surfaces[i] is { } surface)
            {
                SkiaCensus.Release(surface);
            }

            if (_targets[i] is { } target)
            {
                SkiaCensus.Release(target);
            }
        }

        _surfaces = [];
        _targets = [];
        _images = [];
        _presentedAt = [];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var api = _device.Api;
        _ = api.DeviceWaitIdle(_device.Device);
        ReleaseImages();
        if (_swapchain.Handle != 0)
        {
            _device.Swapchains.DestroySwapchain(_device.Device, _swapchain, null);
        }

        api.DestroyCommandPool(_device.Device, _pool, null);
        api.DestroyFence(_device.Device, _fence, null);
        SDL.VulkanDestroySurface((nint)_device.Instance.Handle, _rawSurface, 0);
    }
}
