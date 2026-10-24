using Basin.Render.Skia;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// The Skia Graphite renderer on Vulkan. It draws on a DRM render node and runs on the compositor
/// thread only.
/// </summary>
public sealed unsafe class SkiaGraphiteRenderer : IRenderer
{
    private readonly VulkanDevice _device;
    private readonly SKGraphiteVkBackendContext _backendContext;
    private readonly SKGraphiteContext _context;
    private readonly SKGraphiteRecorder _recorder;
    private readonly SKRuntimeEffect _additiveEffect;

    private const string AdditiveSksl = "half4 main(half4 color) { return half4(color.rgb, 0); }";
    private readonly SKGraphiteVkGetProcedureAddressDelegate _getProc;
    private readonly Dictionary<IBuffer, SkiaGraphiteTarget> _targets = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly SkiaGraphiteRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly SkiaVulkanSync _sync;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SkiaBackdropCopy _backdropCopy;
    private SKGraphiteBackendTexture? _backdropCopyBackend;
    private SKImage? _backdropCopyImage;

    internal readonly List<VulkanDeviceImage> ForeignThisFrame = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaGraphiteRenderer"/> class. It throws <see
    /// cref="NotSupportedException"/> when the SkiaSharp native build has no Graphite Vulkan backend.
    /// </summary>
    /// <param name="drmNodePath">The DRM node whose Vulkan device the renderer opens.</param>
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
        _additiveEffect = SkiaCensus.Track(SKRuntimeEffect.CreateColorFilter(AdditiveSksl, out var errors)
            ?? throw new InvalidOperationException($"the additive filter failed to compile: {errors}"));
        AdditiveFilter = SkiaCensus.Track(_additiveEffect.ToColorFilter());
        _paint = SkiaCensus.Track(new SKPaint());
        _pass = new SkiaGraphiteRenderPass(this, _paint);
        _sync = new SkiaVulkanSync(_device);
        _backdropCopy = new SkiaBackdropCopy(_device);
    }

    /// <summary>
    /// Gets the Vulkan device that the renderer draws on. The renderer owns it.
    /// </summary>
    public VulkanDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    /// <summary>
    /// Creates a render stack with a new <see cref="SkiaGraphiteRenderer"/> and an allocator on the
    /// same device. The renderer is disposed when the allocator cannot be made.
    /// </summary>
    /// <param name="renderNodePath">The DRM render node to open, for example <c>/dev/dri/renderD128</c>.</param>
    /// <returns>The stack, which owns the renderer and the allocator.</returns>
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

    /// <summary>
    /// Gets the Graphite context. The renderer owns it.
    /// </summary>
    public SKGraphiteContext Context => _context;

    /// <summary>
    /// Gets the Graphite recorder that every surface and image of the renderer records into. The
    /// renderer owns it.
    /// </summary>
    public SKGraphiteRecorder Recorder => _recorder;

    internal SKColorFilter AdditiveFilter { get; }

    internal SkiaVulkanSync Sync => _sync;

    /// <summary>
    /// Gets a value indicating whether a pass can run backdrop effects. It is always <see
    /// langword="true"/> on this renderer.
    /// </summary>
    public bool SupportsBackdropEffects => true;

    internal SkiaBackdropCopy BackdropCopy => _backdropCopy;

    internal SKImage? BackdropImage(Silk.NET.Vulkan.Extent2D extent)
    {
        if (_backdropCopyImage is { } cached && _backdropCopy.Matches(extent))
        {
            return cached;
        }

        ReleaseBackdropCopy();
        _backdropCopy.Create(extent);
        _backdropCopyBackend = SkiaCensus.Track(SKGraphiteBackendTexture.CreateVulkan(
            (int)extent.Width, (int)extent.Height,
            new SKGraphiteVkTextureInfo
            {
                SampleCount = 1,
                Format = (int)SkiaVulkanRenderer.VkFormatR16G16B16A16Sfloat,
                ImageTiling = (int)SkiaVulkanRenderer.VkImageTilingOptimal,
                ImageUsageFlags = SkiaVulkanRenderer.VkUsageRenderTarget,
                SharingMode = 0,
                AspectMask = 1,
            },
            (int)SkiaVulkanRenderer.VkImageLayoutShaderReadOnly,
            _device.QueueFamily,
            (nint)_backdropCopy.Image.Handle));
        var wrapped = SKImage.FromTexture(_recorder, _backdropCopyBackend, SKColorType.RgbaF16, SKAlphaType.Premul);
        if (wrapped is null)
        {
            ReleaseBackdropCopy();
            return null;
        }

        _backdropCopyImage = SkiaCensus.Track(wrapped);
        return _backdropCopyImage;
    }

    private void ReleaseBackdropCopy()
    {
        SkiaCensus.Release(_backdropCopyImage);
        SkiaCensus.Release(_backdropCopyBackend);
        _backdropCopyImage = null;
        _backdropCopyBackend = null;
        _backdropCopy.Release();
    }

    /// <inheritdoc/>
    public DrmFormatSet DmabufTextureFormats => _device.SampleableRgbFormats;

    /// <inheritdoc/>
    public DrmFormatSet DmabufRenderFormats => _device.BgraRenderableFormats;

    /// <summary>
    /// Gets how the renderer takes a color conversion. It is always <see
    /// cref="ColorTransformCapability.Decomposed"/>.
    /// </summary>
    public ColorTransformCapability ColorTransform => ColorTransformCapability.Decomposed;

    internal SkiaColorTransforms ColorTransforms { get; } = new();

    /// <inheritdoc/>
    public bool WaitsOnGpu => _device.WaitsOnGpu;

    /// <summary>
    /// Gets how much a signal fence of a pass promises. It is always <see
    /// cref="RenderFencePrecision.Context"/>.
    /// </summary>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    /// <summary>
    /// Imports a dmabuf as a GPU texture, or wraps a memory buffer that uploads on each change. It
    /// never throws for a foreign or hostile buffer.
    /// </summary>
    /// <param name="buffer">The buffer to import.</param>
    /// <returns>The texture, which the caller disposes, or <see langword="null"/> when the dmabuf format is not in <see cref="DmabufTextureFormats"/> or the import fails.</returns>
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

    /// <summary>
    /// Compiles the SkSL variant of a pixel shader. A source that does not compile throws with the
    /// compiler log.
    /// </summary>
    /// <param name="source">The shader source. Only its SkSL text is read.</param>
    /// <param name="uniforms">The uniforms that the shader declares after the uniforms of basin, in order.</param>
    /// <returns>The shader, which the caller disposes, or <see langword="null"/> when the source has no SkSL.</returns>
    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut, _recorder);
    }

    /// <summary>
    /// Begins a pass that draws into a dmabuf or a CPU buffer. The renderer caches one Skia surface per
    /// target until the buffer is destroyed. A lost device drops every cached target and throws <see
    /// cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="target">A BGRA dmabuf or a 4-byte memory buffer to draw into.</param>
    /// <param name="options">The fences and the output color description of the pass.</param>
    /// <returns>The renderer's single pass object. The caller must submit it before the next pass begins.</returns>
    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (_context.IsDeviceLost)
        {
            DropTargets();
            throw new InvalidOperationException("The Graphite context reports device loss; cached targets were dropped.");
        }

        _sync.WaitFence(options.WaitFenceFd);
        if (_targets.TryGetValue(target, out var entry) && entry.IsCpuReadback && entry.SeenWrites != target.WriteCount)
        {
            _targets.Remove(target);
            entry.Dispose();
            entry = null;
        }

        entry ??= ImportTarget(target);

        _pass.Begin(target, entry, options.SignalFenceFd, options.ColorDescription);
        return _pass;
    }

    private SkiaGraphiteTarget ImportTarget(IBuffer target)
    {
        var entry = SkiaGraphiteTarget.Create(_device, _recorder, target);
        entry.SeenWrites = target.WriteCount;
        _targets[target] = entry;
        if (_watched.Add(target))
        {
            target.Destroyed += () =>
            {
                _watched.Remove(target);
                if (_targets.Remove(target, out var dead))
                {
                    dead.Dispose();
                }
            };
        }

        return entry;
    }

    /// <summary>
    /// Frees every cached target, the Graphite recorder and context, and the Vulkan device. It waits
    /// for the device to idle first.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        DropTargets();
        ReleaseBackdropCopy();
        ColorTransforms.Dispose();
        SkiaCensus.Release(_paint);
        SkiaCensus.Release(AdditiveFilter);
        SkiaCensus.Release(_additiveEffect);
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
