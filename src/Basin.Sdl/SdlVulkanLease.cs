using Basin.Render.Skia;
using SDL3;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// Draws into the images of a swapchain that the lease owns, with FIFO present.
/// Every image that Skia touches is in the color attachment layout, so the lease never reads an image back.
/// </summary>
public sealed unsafe class SdlVulkanLease : ISdlRenderLease
{
    private const ulong AcquireTimeoutNs = 1_000_000_000UL;

    private readonly SdlVulkanDevice _device;
    private readonly SdlWindow _window;
    private nint _rawSurface;
    private SurfaceKHR _surface;
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
    private bool _held;
    private int _age;
    private bool _stale = true;
    private Fence _fence;
    private CommandPool _pool;
    private CommandBuffer _commands;
    private bool _disposed;
    private bool _warnedAcquire;
    private bool _surfaceLost;
    private bool _rebuildingLostSurface;
    private bool _stopped;

    internal SdlVulkanLease(SdlVulkanDevice device, SdlWindow window)
    {
        _device = device;
        _window = window;
        if (!SDL.VulkanCreateSurface(window.Handle, (nint)device.Instance.Handle, 0, out _rawSurface))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed with \"{SDL.GetError()}\".");
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

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Vulkan;

    private SdlHdrState _requested = SdlHdrState.Sdr;
    private bool _hdrActive;
    private bool _pendingHdr;

    /// <summary>
    /// Gets the HDR state that the next frame presents.
    /// While the swapchain waits for a rebuild, it is the state that the rebuild will choose from the formats that the surface offers.
    /// </summary>
    public SdlHdrState Hdr => (_stale ? _pendingHdr : _hdrActive) ? _requested : SdlHdrState.Sdr;

    /// <summary>
    /// Requests HDR10 or SDR. A change of the enabled flag marks the swapchain for a rebuild at the next <see cref="Begin"/>.
    /// <see cref="Hdr"/> reports the state of that rebuild at once.
    /// </summary>
    /// <param name="state">The requested state.</param>
    /// <returns><see langword="false"/> when HDR is requested and the surface offers no HDR10 format that Skia can draw.</returns>
    public bool TrySetHdr(in SdlHdrState state)
    {
        if (state.Enabled != _requested.Enabled)
        {
            _stale = true;
        }

        _requested = state;
        _pendingHdr = state.Enabled && OffersHdr10();
        return !state.Enabled || _pendingHdr;
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

    private static ReadOnlySpan<Format> Hdr10Formats => [Format.A2B10G10R10UnormPack32, Format.A2R10G10B10UnormPack32, Format.R16G16B16A16Sfloat];

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

    /// <summary>
    /// Gets the Vulkan format of the current swapchain images. It is undefined before the first <see cref="Begin"/>.
    /// </summary>
    public Format SurfaceFormat => _format;

    /// <summary>
    /// Sets the size of the swapchain. A new size marks the swapchain for a rebuild at the next <see cref="Begin"/>.
    /// </summary>
    /// <param name="pixelWidth">The width in device pixels. A value below 1 counts as 1.</param>
    /// <param name="pixelHeight">The height in device pixels. A value below 1 counts as 1.</param>
    public void Resize(int pixelWidth, int pixelHeight)
    {
        var extent = new Extent2D((uint)Math.Max(1, pixelWidth), (uint)Math.Max(1, pixelHeight));
        if (extent.Width != _extent.Width || extent.Height != _extent.Height)
        {
            _extent = extent;
            _stale = true;
        }
    }

    /// <summary>
    /// Does nothing, because Vulkan has no current context.
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool MakeCurrent() => true;

    /// <summary>
    /// Does not read a swapchain image back. The caller must render the frame offscreen instead.
    /// </summary>
    /// <param name="info">The size and format to read. It is not used.</param>
    /// <param name="pixels">The memory that receives the pixels. It is not used.</param>
    /// <param name="rowBytes">The stride of <paramref name="pixels"/> in bytes. It is not used.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) => false;

    /// <summary>
    /// Acquires the next swapchain image and starts a frame on it. The acquire waits on the CPU for at most one second.
    /// After <see cref="Discard"/>, it starts the frame on the image that it kept, with the same age, unless the swapchain needs a rebuild.
    /// An out-of-date swapchain is rebuilt once. A lost surface is created again with a new swapchain once.
    /// When that rebuild fails, the lease stops and every later call returns <see langword="false"/>. A device loss abandons the context.
    /// </summary>
    /// <param name="canvas">The canvas over the acquired image, in device pixels.</param>
    /// <param name="context">The shared Skia context of the device.</param>
    /// <param name="age">The number of frames since the image was last presented, or 0 for an image that was never presented.</param>
    /// <returns><see langword="false"/> when the context is missing or lost, the lease stopped, or no image can be acquired.</returns>
    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas = null!;
        age = 0;
        context = _device.Context;
        if (_stopped || context is null || context.IsAbandoned)
        {
            return false;
        }

        if (_stale && !Rebuild())
        {
            return false;
        }

        if (_held)
        {
            _held = false;
            _acquired = true;
            age = _age;
            canvas = SurfaceAt(_index, context).Canvas;
            return true;
        }

        var result = Acquire();
        if (result is Result.ErrorSurfaceLostKhr)
        {
            _surfaceLost = true;
        }

        if (result is Result.ErrorOutOfDateKhr or Result.ErrorSurfaceLostKhr)
        {
            if (!Rebuild())
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
            if (!_warnedAcquire)
            {
                _warnedAcquire = true;
                Log.Warn($"vulkan: swapchain acquire returned {result}, frame skipped");
            }

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

        _age = age;
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
            ?? throw new InvalidOperationException($"Ganesh rejected swapchain image {index} in {_format}.");
        _targets[index] = target;
        _surfaces[index] = SkiaCensus.Track(surface);
        return surface;
    }

    /// <summary>
    /// Flushes Skia synchronously, moves the image to the present layout and queues it. Without an acquired image it does nothing.
    /// A device loss abandons the context.
    /// </summary>
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
        else if (result is Result.ErrorSurfaceLostKhr)
        {
            _surfaceLost = true;
            _stale = true;
        }
        else if (result is Result.ErrorDeviceLost)
        {
            context.AbandonContext(false);
        }
    }

    /// <summary>
    /// Ends the frame without a present and keeps the acquired image, so that the next <see cref="Begin"/> starts on it without a new acquire.
    /// Without an acquired image it does nothing.
    /// </summary>
    public void Discard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_acquired)
        {
            return;
        }

        _acquired = false;
        _held = true;
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

    private bool Rebuild()
    {
        _rebuildingLostSurface = _surfaceLost;
        try
        {
            return Recreate();
        }
        catch (InvalidOperationException error) when (_rebuildingLostSurface)
        {
            _stopped = true;
            Log.Error(error, $"vulkan: window surface lost and not rebuilt, window stops presenting");
            return false;
        }
    }

    private bool Recreate()
    {
        var api = _device.Api;
        var surfaces = _device.Surfaces;
        _ = api.DeviceWaitIdle(_device.Device);
        ReleaseImages();
        _held = false;
        _acquired = false;
        var replaced = false;
        if (_surfaceLost)
        {
            ReplaceSurface();
            replaced = true;
        }

        var result = surfaces.GetPhysicalDeviceSurfaceCapabilities(_device.Physical, _surface, out var caps);
        if (result is Result.ErrorSurfaceLostKhr && !replaced)
        {
            _rebuildingLostSurface = true;
            ReplaceSurface();
            result = surfaces.GetPhysicalDeviceSurfaceCapabilities(_device.Physical, _surface, out caps);
        }

        SdlVulkanDevice.Check(result, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
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
        _rebuildingLostSurface = false;
        return true;
    }

    private void ReplaceSurface()
    {
        _surfaceLost = false;
        if (_swapchain.Handle != 0)
        {
            _device.Swapchains.DestroySwapchain(_device.Device, _swapchain, null);
            _swapchain = default;
        }

        var instance = (nint)_device.Instance.Handle;
        SDL.VulkanDestroySurface(instance, _rawSurface, 0);
        _rawSurface = 0;
        _surface = default;
        if (!SDL.VulkanCreateSurface(_window.Handle, instance, 0, out var raw))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed with \"{SDL.GetError()}\".");
        }

        _rawSurface = raw;
        _surface = new SurfaceKHR((ulong)raw);
    }

    private (Format Format, SKColorType ColorType, ColorSpaceKHR ColorSpace) ChooseFormat()
    {
        var formats = SurfaceFormats();
        _hdrActive = false;
        if (_requested.Enabled)
        {
            foreach (var wanted in Hdr10Formats)
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

        throw new InvalidOperationException("The Vulkan surface of the SDL window offers no 8-bit sRGB format.");
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

    /// <summary>
    /// Waits for the device to go idle and destroys the swapchain, the command objects and the surface. A second call does nothing.
    /// </summary>
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
        if (_rawSurface != 0)
        {
            SDL.VulkanDestroySurface((nint)_device.Instance.Handle, _rawSurface, 0);
        }
    }
}
