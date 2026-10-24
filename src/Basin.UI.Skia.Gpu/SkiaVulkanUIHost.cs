using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// The Skia chrome host on a Vulkan device. It draws <see cref="SkiaVulkanUISurface"/> instances with Ganesh. Use it on the thread that created it.
/// </summary>
public sealed class SkiaVulkanUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly VulkanDevice _device;
    private readonly IAllocator? _allocator;
    private readonly GRContext _context;
    private readonly bool _ownsContext;
    private readonly GRVkExtensions? _ownExtensions;
    private readonly GRVkGetProcedureAddressDelegate? _getProc;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaVulkanUIHost"/> class. Without a shared context, the host makes a Ganesh context on the device queue and owns it.
    /// </summary>
    /// <param name="device">The Vulkan device that the surfaces draw on. The host does not own it.</param>
    /// <param name="sharedContext">The Ganesh context of a Skia renderer on the same device, or <see langword="null"/>.</param>
    /// <param name="allocator">The allocator of the dmabufs, which the host does not own. When it is <see langword="null"/>, the host produces memory buffers only.</param>
    /// <exception cref="InvalidOperationException">Ganesh rejects the context.</exception>
    public SkiaVulkanUIHost(VulkanDevice device, GRContext? sharedContext = null, IAllocator? allocator = null)
    {
        _device = device;
        _allocator = allocator;
        if (sharedContext is not null)
        {
            _context = sharedContext;
            _ownsContext = false;
            return;
        }

        _getProc = GetProcedureAddress;
        _ownExtensions = SkiaCensus.Track(GRVkExtensions.Create(
            _getProc, device.Instance.Handle, device.Physical.Handle, [], device.EnabledExtensions));
        var backendContext = new GRVkBackendContext
        {
            VkInstance = device.Instance.Handle,
            VkPhysicalDevice = device.Physical.Handle,
            VkDevice = device.Device.Handle,
            VkQueue = device.Queue.Handle,
            GraphicsQueueIndex = device.QueueFamily,
            MaxAPIVersion = Silk.NET.Vulkan.Vk.Version12,
            Extensions = _ownExtensions,
            GetProcedureAddress = _getProc,
        };
        using (backendContext)
        {
            _context = GRContext.CreateVulkan(backendContext)
                ?? throw new InvalidOperationException("Ganesh refused a context on this device.");
        }

        SkiaCensus.Track(_context);
        _ownsContext = true;
    }

    /// <summary>
    /// Gets <see cref="ISkiaUISurface"/>, which every surface of the host implements.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    /// <summary>
    /// Gets <see cref="UITargetKind.Dmabuf"/> when the host has an allocator, otherwise <see cref="UITargetKind.Memory"/>.
    /// </summary>
    public UITargetKind Produces => _allocator is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

    private ulong[]? _modifiers;

    private ulong[] RenderableModifiers() => _modifiers ??=
        [.. _device.RenderableFormats.ModifiersOf(DrmFormat.Argb8888).Where(m => m != DrmFormatSet.ModifierInvalid)];

    /// <summary>
    /// Gets <see langword="null"/>, because the host never has work due.
    /// </summary>
    public long? NextDueMillis => null;

    /// <summary>
    /// Never occurs. The host has no work of its own.
    /// </summary>
    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Creates a configured <see cref="SkiaVulkanUISurface"/>. The caller owns the surface and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the host is disposed, does not produce the kind, or the allocation fails.</returns>
    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || (options.Target & Produces) == 0)
        {
            return null;
        }

        var context = _context;
        var surface = new SkiaVulkanUISurface(
            _device,
            buffer => new GaneshDrawTarget(SkiaVulkanTarget.Create(_device, context, buffer)),
            () => context.Flush(submit: true, synchronous: false),
            options.Target == UITargetKind.Dmabuf ? _allocator : null,
            RenderableModifiers());
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        return surface;
    }

    /// <summary>
    /// Does nothing. The caller draws each surface.
    /// </summary>
    public void Pump()
    {
    }

    /// <summary>
    /// Abandons and releases the Ganesh context when the host owns it. The surfaces that the host made are not disposed.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsContext)
        {
            _context.AbandonContext(releaseResources: true);
            SkiaCensus.Release(_context);
            SkiaCensus.Release(_ownExtensions);
        }
    }

    private nint GetProcedureAddress(string name, nint instance, nint device)
    {
        if (device != 0)
        {
            return _device.Api.GetDeviceProcAddr(new Silk.NET.Vulkan.Device(device), name);
        }

        return _device.Api.GetInstanceProcAddr(new Silk.NET.Vulkan.Instance(instance), name);
    }
}
