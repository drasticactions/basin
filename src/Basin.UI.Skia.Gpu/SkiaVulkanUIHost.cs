using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

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

    public UITargetKind Produces => _allocator is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

    private ulong[]? _modifiers;

    private ulong[] RenderableModifiers() => _modifiers ??=
        [.. _device.RenderableFormats.ModifiersOf(DrmFormat.Argb8888).Where(m => m != DrmFormatSet.ModifierInvalid)];

    public long? NextDueMillis => null;

    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

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

    public void Pump()
    {
    }

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
