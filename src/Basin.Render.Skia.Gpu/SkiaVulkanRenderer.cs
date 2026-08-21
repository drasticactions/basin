using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using SkiaSharp;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Skia;

public sealed unsafe class SkiaVulkanRenderer : IRenderer
{
    public const uint VkFormatB8G8R8A8Unorm = 44;
    public const uint VkImageTilingOptimal = 0;
    public const uint VkImageTilingDrmFormatModifier = 1000158000;
    public const uint VkImageLayoutGeneral = 1;
    public const uint VkUsageTransferSrc = 0x1;
    public const uint VkUsageTransferDst = 0x2;
    public const uint VkUsageSampled = 0x4;
    public const uint VkUsageColorAttachment = 0x10;

    public const uint VkUsageRenderTarget =
        VkUsageColorAttachment | VkUsageTransferSrc | VkUsageTransferDst | VkUsageSampled;

    public const uint VkUsageTexture = VkUsageSampled | VkUsageTransferSrc | VkUsageTransferDst;

    private readonly VulkanDevice _device;
    private readonly GRVkBackendContext _backendContext;
    private readonly GRVkExtensions _extensions;
    private readonly GRContext _context;
    private readonly GRVkGetProcedureAddressDelegate _getProc;
    private readonly Dictionary<IBuffer, SkiaVulkanTarget> _targets = [];
    private readonly SkiaVulkanRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SkiaVulkanSync _sync;

    internal readonly List<VulkanDeviceImage> ForeignThisFrame = [];

    public SkiaVulkanRenderer(string drmNodePath)
    {
        _device = new VulkanDevice(drmNodePath, ["VK_KHR_external_semaphore_fd", "VK_KHR_external_semaphore"]);
        _getProc = GetProcedureAddress;

        _extensions = SkiaCensus.Track(GRVkExtensions.Create(
            _getProc, _device.Instance.Handle, _device.Physical.Handle, [], _device.EnabledExtensions));
        _backendContext = new GRVkBackendContext
        {
            VkInstance = _device.Instance.Handle,
            VkPhysicalDevice = _device.Physical.Handle,
            VkDevice = _device.Device.Handle,
            VkQueue = _device.Queue.Handle,
            GraphicsQueueIndex = _device.QueueFamily,
            MaxAPIVersion = Vk.Version12,
            Extensions = _extensions,
            GetProcedureAddress = _getProc,
        };
        var context = GRContext.CreateVulkan(_backendContext);
        if (context is null)
        {
            _backendContext.Dispose();
            SkiaCensus.Release(_extensions);
            _device.Dispose();
            throw new InvalidOperationException("Ganesh could not create a Vulkan context on this device.");
        }

        _context = SkiaCensus.Track(context);
        _paint = SkiaCensus.Track(new SKPaint());
        _pass = new SkiaVulkanRenderPass(this, _paint);
        _sync = new SkiaVulkanSync(_device);
    }

    public VulkanDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new SkiaVulkanRenderer(renderNodePath);
        try
        {
            return new RenderStack(renderer, renderer.Device.CreateAllocator());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    public GRContext Context => _context;

    internal SkiaVulkanSync Sync => _sync;

    public DrmFormatSet DmabufTextureFormats => _device.SampleableRgbFormats;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    public bool WaitsOnGpu => _device.WaitsOnGpu;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        if (buffer.TryGetDmabuf(out var attributes))
        {
            return DmabufTextureFormats.Contains(attributes.Format)
                ? SkiaVulkanDmabufTexture.TryImport(this, attributes)
                : null;
        }

        return new SkiaVulkanShmTexture(this, buffer);
    }

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut);
    }

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (_context.IsAbandoned)
        {
            DropTargets();
            throw new InvalidOperationException("The Ganesh context is abandoned; cached targets were dropped.");
        }

        _sync.WaitFence(options.WaitFenceFd);
        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _pass.Begin(target, entry, options.SignalFenceFd);
        return _pass;
    }

    private SkiaVulkanTarget ImportTarget(IBuffer target)
    {
        var entry = SkiaVulkanTarget.Create(_device, _context, target);
        _targets[target] = entry;
        target.Destroyed += () =>
        {
            if (_targets.Remove(target, out var dead))
            {
                dead.Dispose();
            }
        };
        return entry;
    }

    public void Dispose()
    {
        _thread.Assert();
        DropTargets();
        SkiaCensus.Release(_paint);

        _context.AbandonContext(releaseResources: true);
        SkiaCensus.Release(_context);
        _backendContext.Dispose();
        SkiaCensus.Release(_extensions);

        _ = _device.Api.DeviceWaitIdle(_device.Device);
        _sync.Dispose();
        _device.Dispose();
    }

    private void DropTargets()
    {
        foreach (var entry in _targets.Values)
        {
            entry.Dispose();
        }

        _targets.Clear();
    }

    private nint GetProcedureAddress(string name, nint instance, nint device)
    {
        if (device != 0)
        {
            return _device.Api.GetDeviceProcAddr(new Device(device), name);
        }

        return _device.Api.GetInstanceProcAddr(new Instance(instance), name);
    }
}
