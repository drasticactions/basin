using System.Runtime.InteropServices;
using Basin.Render.Skia;
using SDL3;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using SkiaSharp;
using static Basin.Sdl.SdlLog;

namespace Basin.Sdl;

/// <summary>
/// The render device for the <see cref="SdlRenderDevices.Vulkan"/> name. On Linux it shares the render node device with a dmabuf importer when that device can present.
/// Otherwise it builds a device of its own through the SDL Vulkan loader, with no dmabuf import.
/// </summary>
public sealed unsafe class SdlVulkanDevice : ISdlRenderDevice
{
    /// <summary>
    /// The render node that the device tries first for dmabuf import.
    /// </summary>
    public const string DefaultRenderNode = "/dev/dri/renderD128";

    /// <summary>
    /// The instance extension that the device enables when it is offered, so that a lease can present HDR10.
    /// </summary>
    public const string SwapchainColorspace = "VK_EXT_swapchain_colorspace";

    private readonly string? _renderNode;
    private SkiaVulkanRenderer? _skia;
    private GRContext? _context;
    private GRVkBackendContext? _backend;
    private GRVkExtensions? _extensions;
    private GRVkGetProcedureAddressDelegate? _getProc;
    private SdlVulkanNativeContext? _native;
    private bool _ownsDevice;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlVulkanDevice"/> class. It creates no Vulkan object until the first lease.
    /// </summary>
    /// <param name="renderNode">The render node to share with the dmabuf importer. A path that does not exist, or <see langword="null"/>, turns the sharing off.</param>
    public SdlVulkanDevice(string? renderNode = DefaultRenderNode)
    {
        _renderNode = renderNode is not null && File.Exists(renderNode) ? renderNode : null;
    }

    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Vulkan;

    /// <inheritdoc/>
    public SDL.WindowFlags WindowFlags => SDL.WindowFlags.Vulkan;

    /// <inheritdoc/>
    public GRContext? Context => _context;

    /// <inheritdoc/>
    public nint EglDisplay => 0;

    /// <summary>
    /// Gets the Skia Vulkan renderer that owns the shared render node device. It is <see langword="null"/> when the device runs standalone or before the first lease.
    /// </summary>
    public IRenderer? DmabufRenderer => _skia;

    /// <inheritdoc/>
    public string? RenderNode => _skia is null ? null : _renderNode;

    internal Vk Api { get; private set; } = null!;

    internal Instance Instance { get; private set; }

    internal PhysicalDevice Physical { get; private set; }

    internal Device Device { get; private set; }

    internal Queue Queue { get; private set; }

    internal uint QueueFamily { get; private set; }

    internal KhrSurface Surfaces { get; private set; } = null!;

    internal KhrSwapchain Swapchains { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the Skia context was abandoned after a device loss.
    /// </summary>
    public bool IsLost => _context is { IsAbandoned: true };

    /// <summary>
    /// Creates a Vulkan lease for a window. The first call builds the instance, the device and the Skia context.
    /// </summary>
    /// <param name="window">A window that was created with <see cref="WindowFlags"/>.</param>
    /// <returns>A new lease that the caller owns and must dispose before the window.</returns>
    /// <exception cref="InvalidOperationException">Vulkan or Skia cannot build a device that presents to the window.</exception>
    public ISdlRenderLease CreateLease(SdlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_context is null)
        {
            Initialize(window);
        }

        return new SdlVulkanLease(this, window);
    }

    private bool _libraryHeld;

    private void Initialize(SdlWindow window)
    {
        if (!_libraryHeld)
        {
            _libraryHeld = SDL.VulkanLoadLibrary(null);
        }

        var instanceExtensions = SDL.VulkanGetInstanceExtensions(out _) ??
            throw new InvalidOperationException($"SDL names no Vulkan instance extensions. SDL reports \"{SDL.GetError()}\".");
        if (OffersInstanceExtension(SwapchainColorspace))
        {
            instanceExtensions = [.. instanceExtensions, SwapchainColorspace];
        }
        if (OperatingSystem.IsLinux() && _renderNode is not null)
        {
            try
            {
                InitializeOnRenderNode(window, instanceExtensions, _renderNode);
                return;
            }
            catch (Exception error) when (error is InvalidOperationException or DllNotFoundException)
            {
                Log.Warn(error, $"vulkan: lease cannot share {_renderNode} with the dmabuf importer, lease uses a device of its own");
                ReleaseContext();
            }
        }

        InitializeStandalone(window, instanceExtensions);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private void InitializeOnRenderNode(SdlWindow window, string[] instanceExtensions, string renderNode)
    {
        var skia = new SkiaVulkanRenderer(renderNode, instanceExtensions, ["VK_KHR_swapchain"]);
        try
        {
            var device = skia.Device;
            if (!device.EnabledExtensions.Contains("VK_KHR_swapchain"))
            {
                throw new InvalidOperationException("The render node device offers no VK_KHR_swapchain.");
            }

            Bind(device.Api, device.Instance, device.Physical, device.Device, device.Queue, device.QueueFamily);
            if (!PresentsTo(window))
            {
                throw new InvalidOperationException("The render node device cannot present to the SDL window.");
            }

            _skia = skia;
            _context = skia.Context;
            Log.Info($"vulkan: lease on {renderNode} with dmabuf import");
        }
        catch
        {
            skia.Dispose();
            throw;
        }
    }

    private void Bind(Vk api, Instance instance, PhysicalDevice physical, Device device, Queue queue, uint family)
    {
        Api = api;
        Instance = instance;
        Physical = physical;
        Device = device;
        Queue = queue;
        QueueFamily = family;
        if (!api.TryGetInstanceExtension(instance, out KhrSurface surfaces) ||
            !api.TryGetDeviceExtension(instance, device, out KhrSwapchain swapchains))
        {
            throw new InvalidOperationException("The Vulkan surface or swapchain functions are missing.");
        }

        Surfaces = surfaces;
        Swapchains = swapchains;
    }

    private bool PresentsTo(SdlWindow window)
    {
        if (!SDL.VulkanCreateSurface(window.Handle, (nint)Instance.Handle, 0, out var raw))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed with \"{SDL.GetError()}\".");
        }

        var surface = new SurfaceKHR((ulong)raw);
        try
        {
            Surfaces.GetPhysicalDeviceSurfaceSupport(Physical, QueueFamily, surface, out var supported);
            return supported;
        }
        finally
        {
            SDL.VulkanDestroySurface((nint)Instance.Handle, raw, 0);
        }
    }

    private void InitializeStandalone(SdlWindow window, string[] instanceExtensions)
    {
        _native = new SdlVulkanNativeContext();
        var api = new Vk(_native);
        var appName = (byte*)SilkMarshal.StringToPtr("basin");
        var names = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions);
        try
        {
            var app = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                PEngineName = appName,
                ApiVersion = Vk.Version11,
            };
            var info = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &app,
                EnabledExtensionCount = (uint)instanceExtensions.Length,
                PpEnabledExtensionNames = names,
            };
            Check(api.CreateInstance(in info, null, out var instance), "vkCreateInstance");
            _native.Instance = (nint)instance.Handle;
            Api = api;
            Instance = instance;
            if (!api.TryGetInstanceExtension(instance, out KhrSurface surfaces))
            {
                throw new InvalidOperationException("The Vulkan instance has no VK_KHR_surface.");
            }

            Surfaces = surfaces;
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)names);
        }

        if (!SDL.VulkanCreateSurface(window.Handle, (nint)Instance.Handle, 0, out var raw))
        {
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed with \"{SDL.GetError()}\".");
        }

        var surface = new SurfaceKHR((ulong)raw);
        try
        {
            (Physical, QueueFamily) = PickDevice(surface);
        }
        finally
        {
            SDL.VulkanDestroySurface((nint)Instance.Handle, raw, 0);
        }

        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        string[] deviceExtensions = ["VK_KHR_swapchain"];
        var deviceNames = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);
        try
        {
            var deviceInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = (uint)deviceExtensions.Length,
                PpEnabledExtensionNames = deviceNames,
            };
            Check(Api.CreateDevice(Physical, in deviceInfo, null, out var device), "vkCreateDevice");
            Api.GetDeviceQueue(device, QueueFamily, 0, out var queue);
            Bind(Api, Instance, Physical, device, queue, QueueFamily);
            _ownsDevice = true;
        }
        finally
        {
            SilkMarshal.Free((nint)deviceNames);
        }

        _getProc = GetProcedureAddress;
        _extensions = SkiaCensus.Track(GRVkExtensions.Create(
            _getProc, (nint)Instance.Handle, Physical.Handle, instanceExtensions, deviceExtensions));
        _backend = new GRVkBackendContext
        {
            VkInstance = (nint)Instance.Handle,
            VkPhysicalDevice = Physical.Handle,
            VkDevice = Device.Handle,
            VkQueue = Queue.Handle,
            GraphicsQueueIndex = QueueFamily,
            MaxAPIVersion = Vk.Version11,
            Extensions = _extensions,
            GetProcedureAddress = _getProc,
        };
        _context = GRContext.CreateVulkan(_backend) ??
            throw new InvalidOperationException("Ganesh could not create a Vulkan context on the SDL device.");
        SkiaCensus.Track(_context);
        Log.Info($"vulkan: lease on a device of its own");
    }

    private static bool OffersInstanceExtension(string name)
    {
        try
        {
            using var native = new SdlVulkanNativeContext();
            using var api = new Vk(native);
            uint count = 0;
            if (api.EnumerateInstanceExtensionProperties((byte*)null, ref count, null) != Result.Success)
            {
                return false;
            }

            var properties = new ExtensionProperties[count];
            fixed (ExtensionProperties* list = properties)
            {
                _ = api.EnumerateInstanceExtensionProperties((byte*)null, ref count, list);
            }

            foreach (var property in properties)
            {
                if (SilkMarshal.PtrToString((nint)property.ExtensionName) == name)
                {
                    return true;
                }
            }

            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private (PhysicalDevice Device, uint Family) PickDevice(SurfaceKHR surface)
    {
        uint count = 0;
        Check(Api.EnumeratePhysicalDevices(Instance, ref count, null), "vkEnumeratePhysicalDevices");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* list = devices)
        {
            Check(Api.EnumeratePhysicalDevices(Instance, ref count, list), "vkEnumeratePhysicalDevices");
        }

        (PhysicalDevice, uint)? fallback = null;
        foreach (var device in devices)
        {
            Api.GetPhysicalDeviceProperties(device, out var properties);
            uint families = 0;
            Api.GetPhysicalDeviceQueueFamilyProperties(device, ref families, null);
            var list = new QueueFamilyProperties[families];
            fixed (QueueFamilyProperties* familyList = list)
            {
                Api.GetPhysicalDeviceQueueFamilyProperties(device, ref families, familyList);
            }

            for (var i = 0u; i < families; i++)
            {
                if ((list[i].QueueFlags & QueueFlags.GraphicsBit) == 0)
                {
                    continue;
                }

                Surfaces.GetPhysicalDeviceSurfaceSupport(device, i, surface, out var supported);
                if (!supported)
                {
                    continue;
                }

                if (properties.DeviceType != PhysicalDeviceType.Cpu)
                {
                    return (device, i);
                }

                fallback ??= (device, i);
            }
        }

        return fallback ?? throw new InvalidOperationException("No Vulkan device can present to the SDL window.");
    }

    private nint GetProcedureAddress(string name, nint instance, nint device) =>
        device != 0
            ? (nint)Api.GetDeviceProcAddr(new Device(device), name)
            : (nint)Api.GetInstanceProcAddr(new Instance(instance), name);

    internal static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"{what} failed with {result}.");
        }
    }

    private void ReleaseContext()
    {
        if (_skia is { } skia)
        {
            skia.Dispose();
            _skia = null;
            _context = null;
            return;
        }

        if (_context is { } context)
        {
            context.AbandonContext(false);
            SkiaCensus.Release(context);
            _context = null;
        }

        _backend?.Dispose();
        _backend = null;
        if (_extensions is { } extensions)
        {
            SkiaCensus.Release(extensions);
            _extensions = null;
        }

        if (_ownsDevice)
        {
            Api.DestroyDevice(Device, null);
            Api.DestroyInstance(Instance, null);
            _ownsDevice = false;
        }
    }

    /// <inheritdoc/>
    public bool TryRecover()
    {
        if (_disposed || _context is not { IsAbandoned: true })
        {
            return false;
        }

        Log.Warn($"vulkan: device lost, lease builds a new one");
        ReleaseContext();
        return true;
    }

    /// <summary>
    /// Waits for the device to go idle, releases every Vulkan object and the SDL Vulkan library. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_context is { IsAbandoned: false })
        {
            _ = Api.DeviceWaitIdle(Device);
        }

        ReleaseContext();
        if (_libraryHeld)
        {
            _libraryHeld = false;
            SDL.VulkanUnloadLibrary();
        }
    }
}
