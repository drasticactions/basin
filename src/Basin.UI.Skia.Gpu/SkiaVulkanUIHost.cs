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
public sealed class SkiaVulkanUIHost : IUIHost, IUISurfaceObserver, IRendererBound
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<SkiaVulkanUISurface> _surfaces = [];
    private VulkanDevice _device;
    private IAllocator? _allocator;
    private GRContext _context;
    private bool _ownsContext;
    private GRVkExtensions? _ownExtensions;
    private GRVkGetProcedureAddressDelegate? _getProc;
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

        _context = CreateOwnContext(device);
        _ownsContext = true;
    }

    private GRContext CreateOwnContext(VulkanDevice device)
    {
        _getProc ??= GetProcedureAddress;
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
            var context = GRContext.CreateVulkan(backendContext)
                ?? throw new InvalidOperationException("Ganesh refused a context on this device.");
            return SkiaCensus.Track(context);
        }
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

        var surface = new SkiaVulkanUISurface(
            _device,
            WrapFactory(),
            Flusher(),
            options.Target == UITargetKind.Dmabuf ? _allocator : null,
            RenderableModifiers());
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        _surfaces.Add(surface);
        surface.AddObserver(this);
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
        _surfaces.Clear();
        ReleaseOwnContext();
    }

    /// <inheritdoc/>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    /// <inheritdoc/>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (surface is SkiaVulkanUISurface vulkanSurface)
        {
            _surfaces.Remove(vulkanSurface);
        }
    }

    /// <summary>
    /// Moves the host and its live surfaces to the device of a new Vulkan or Skia Vulkan renderer. The surfaces keep their buffers and drop their GPU targets.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    /// <exception cref="InvalidOperationException">The new renderer has no Vulkan device.</exception>
    public void OnRendererReplaced(RendererReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        foreach (var surface in _surfaces)
        {
            surface.ReleaseTargets();
        }

        ReleaseOwnContext();
        switch (replacement.New.Renderer)
        {
            case SkiaVulkanRenderer skiaVk:
                _device = skiaVk.Device;
                _context = skiaVk.Context;
                _ownsContext = false;
                break;
            case Basin.Render.Vulkan.VulkanRenderer vulkan:
                _device = vulkan.Device;
                _context = CreateOwnContext(vulkan.Device);
                _ownsContext = true;
                break;
            default:
                throw new InvalidOperationException($"A Vulkan chrome host cannot move to a {replacement.New.Renderer.GetType().Name}.");
        }

        _allocator = _allocator is null ? null : _device.CreateAllocator();
        _modifiers = null;
        foreach (var surface in _surfaces)
        {
            surface.Rebind(_device, WrapFactory(), Flusher(), _allocator, RenderableModifiers());
        }
    }

    private Func<BufferBase, IVulkanDrawTarget> WrapFactory()
    {
        var device = _device;
        var context = _context;
        return buffer => new GaneshDrawTarget(SkiaVulkanTarget.Create(device, context, buffer));
    }

    private Action Flusher()
    {
        var context = _context;
        return () => context.Flush(submit: true, synchronous: false);
    }

    private void ReleaseOwnContext()
    {
        if (!_ownsContext)
        {
            return;
        }

        _ownsContext = false;
        _context.AbandonContext(releaseResources: true);
        SkiaCensus.Release(_context);
        SkiaCensus.Release(_ownExtensions);
        _ownExtensions = null;
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
