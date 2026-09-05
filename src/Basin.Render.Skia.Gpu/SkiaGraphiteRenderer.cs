using Basin.Render.Skia;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

public sealed unsafe class SkiaGraphiteRenderer : IRenderer
{
    private readonly VulkanDevice _device;
    private readonly SKGraphiteVkBackendContext _backendContext;
    private readonly SKGraphiteContext _context;
    private readonly SKGraphiteRecorder _recorder;
    private readonly SKGraphiteVkGetProcedureAddressDelegate _getProc;
    private readonly Dictionary<IBuffer, SkiaGraphiteTarget> _targets = [];
    private readonly SkiaGraphiteRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly SkiaVulkanSync _sync;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    internal readonly List<VulkanDeviceImage> ForeignThisFrame = [];

    public SkiaGraphiteRenderer(string drmNodePath)
    {
        if (!SKGraphiteContext.IsBackendAvailable(SKGraphiteBackend.Vulkan))
        {
            throw new NotSupportedException("This libSkiaSharp build has no Graphite Vulkan backend.");
        }

        _device = new VulkanDevice(drmNodePath, ["VK_KHR_external_semaphore_fd", "VK_KHR_external_semaphore"]);
        _getProc = GetProcedureAddress;
        _backendContext = new SKGraphiteVkBackendContext
        {
            VkInstance = _device.Instance.Handle,
            VkPhysicalDevice = _device.Physical.Handle,
            VkDevice = _device.Device.Handle,
            VkQueue = _device.Queue.Handle,
            GraphicsQueueIndex = _device.QueueFamily,
            MaxApiVersion = Silk.NET.Vulkan.Vk.Version12,
            GetProcedureAddress = _getProc,
        };
        var context = SKGraphiteContext.CreateVulkan(_backendContext);
        if (context is null)
        {
            _backendContext.Dispose();
            _device.Dispose();
            throw new InvalidOperationException("Graphite could not create a Vulkan context on this device.");
        }

        _context = SkiaCensus.Track(context);

        var recorder = _context.CreateRecorder(256 * 1024 * 1024);
        if (recorder is null)
        {
            SkiaCensus.Release(_context);
            _backendContext.Dispose();
            _device.Dispose();
            throw new InvalidOperationException("Graphite could not create a recorder.");
        }

        _recorder = SkiaCensus.Track(recorder);
        _paint = SkiaCensus.Track(new SKPaint());
        _pass = new SkiaGraphiteRenderPass(this, _paint);
        _sync = new SkiaVulkanSync(_device);
    }

    public VulkanDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new SkiaGraphiteRenderer(renderNodePath);
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

    public SKGraphiteContext Context => _context;

    public SKGraphiteRecorder Recorder => _recorder;

    internal SkiaVulkanSync Sync => _sync;

    public DrmFormatSet DmabufTextureFormats => _device.SampleableRgbFormats;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Decomposed;

    internal SkiaColorTransforms ColorTransforms { get; } = new();

    public bool WaitsOnGpu => _device.WaitsOnGpu;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        if (buffer.TryGetDmabuf(out var attributes))
        {
            return DmabufTextureFormats.Contains(attributes.Format)
                ? SkiaGraphiteDmabufTexture.TryImport(this, attributes)
                : null;
        }

        return new SkiaGraphiteShmTexture(this, buffer);
    }

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut, _recorder);
    }

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (_context.IsDeviceLost)
        {
            DropTargets();
            throw new InvalidOperationException("The Graphite context reports device loss; cached targets were dropped.");
        }

        _sync.WaitFence(options.WaitFenceFd);
        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _pass.Begin(target, entry, options.SignalFenceFd, options.ColorDescription);
        return _pass;
    }

    private SkiaGraphiteTarget ImportTarget(IBuffer target)
    {
        var entry = SkiaGraphiteTarget.Create(_device, _recorder, target);
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
        ColorTransforms.Dispose();
        SkiaCensus.Release(_paint);
        SkiaCensus.Release(_recorder);
        _context.FreeGpuResources();
        SkiaCensus.Release(_context);
        _backendContext.Dispose();
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
            return _device.Api.GetDeviceProcAddr(new Silk.NET.Vulkan.Device(device), name);
        }

        return _device.Api.GetInstanceProcAddr(new Silk.NET.Vulkan.Instance(instance), name);
    }
}
