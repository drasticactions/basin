using Basin.Render.Skia;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using SkiaSharp;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Basin.Render.Skia;

/// <summary>
/// The Skia Ganesh renderer on Vulkan. It draws on a DRM render node and runs on the compositor
/// thread only.
/// </summary>
public sealed unsafe class SkiaVulkanRenderer : IRenderer
{
    /// <summary>
    /// The Vulkan value of <c>VK_FORMAT_B8G8R8A8_UNORM</c>.
    /// </summary>
    public const uint VkFormatB8G8R8A8Unorm = 44;
    /// <summary>
    /// The Vulkan value of <c>VK_FORMAT_R16G16B16A16_SFLOAT</c>.
    /// </summary>
    public const uint VkFormatR16G16B16A16Sfloat = 97;
    /// <summary>
    /// The Vulkan value of <c>VK_IMAGE_TILING_OPTIMAL</c>.
    /// </summary>
    public const uint VkImageTilingOptimal = 0;
    /// <summary>
    /// The Vulkan value of <c>VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT</c>.
    /// </summary>
    public const uint VkImageTilingDrmFormatModifier = 1000158000;
    /// <summary>
    /// The Vulkan value of <c>VK_IMAGE_LAYOUT_GENERAL</c>.
    /// </summary>
    public const uint VkImageLayoutGeneral = 1;
    /// <summary>
    /// The Vulkan value of <c>VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL</c>.
    /// </summary>
    public const uint VkImageLayoutShaderReadOnly = 5;
    /// <summary>
    /// The Vulkan image usage bit <c>VK_IMAGE_USAGE_TRANSFER_SRC_BIT</c>.
    /// </summary>
    public const uint VkUsageTransferSrc = 0x1;
    /// <summary>
    /// The Vulkan image usage bit <c>VK_IMAGE_USAGE_TRANSFER_DST_BIT</c>.
    /// </summary>
    public const uint VkUsageTransferDst = 0x2;
    /// <summary>
    /// The Vulkan image usage bit <c>VK_IMAGE_USAGE_SAMPLED_BIT</c>.
    /// </summary>
    public const uint VkUsageSampled = 0x4;
    /// <summary>
    /// The Vulkan image usage bit <c>VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT</c>.
    /// </summary>
    public const uint VkUsageColorAttachment = 0x10;

    /// <summary>
    /// The image usage flags of a render target: color attachment, transfer source and destination, and
    /// sampled.
    /// </summary>
    public const uint VkUsageRenderTarget =
        VkUsageColorAttachment | VkUsageTransferSrc | VkUsageTransferDst | VkUsageSampled;

    /// <summary>
    /// The image usage flags of a sampled texture: sampled, transfer source and transfer destination.
    /// </summary>
    public const uint VkUsageTexture = VkUsageSampled | VkUsageTransferSrc | VkUsageTransferDst;

    private readonly VulkanDevice _device;
    private readonly GRVkBackendContext _backendContext;
    private readonly GRVkExtensions _extensions;
    private readonly GRContext _context;
    private readonly GRVkGetProcedureAddressDelegate _getProc;
    private readonly Dictionary<IBuffer, SkiaVulkanTarget> _targets = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly SkiaVulkanRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SkiaVulkanSync _sync;
    private readonly SkiaBackdropCopy _backdropCopy;
    private GRBackendTexture? _backdropCopyBackend;
    private SKImage? _backdropCopyImage;

    internal readonly List<VulkanDeviceImage> ForeignThisFrame = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaVulkanRenderer"/> class. It enables no extra
    /// Vulkan extensions.
    /// </summary>
    /// <param name="drmNodePath">The DRM node whose Vulkan device the renderer opens.</param>
    public SkiaVulkanRenderer(string drmNodePath)
        : this(drmNodePath, [], [])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaVulkanRenderer"/> class. The external semaphore
    /// extensions are always enabled in addition to the named ones.
    /// </summary>
    /// <param name="drmNodePath">The DRM node whose Vulkan device the renderer opens.</param>
    /// <param name="instanceExtensions">The extra instance extensions to enable, for example the ones a window surface needs.</param>
    /// <param name="deviceExtensions">The extra device extensions to enable.</param>
    public SkiaVulkanRenderer(string drmNodePath, string[] instanceExtensions, string[] deviceExtensions)
    {
        _device = new VulkanDevice(
            drmNodePath, ["VK_KHR_external_semaphore_fd", "VK_KHR_external_semaphore", .. deviceExtensions], instanceExtensions);
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
        _backdropCopy = new SkiaBackdropCopy(_device);
    }

    /// <summary>
    /// Gets the Vulkan device that the renderer draws on. The renderer owns it.
    /// </summary>
    public VulkanDevice Device => _device;

    /// <summary>
    /// Moves every dmabuf image that a draw used since the last release from the foreign queue to the
    /// renderer's queue. Call it before a Ganesh flush that is outside a render pass.
    /// </summary>
    public void AcquireForeignImages()
    {
        _thread.Assert();
        if (ForeignThisFrame.Count == 0)
        {
            return;
        }

        _device.SubmitImmediate(this, static (renderer, commands) =>
        {
            foreach (var image in renderer.ForeignThisFrame)
            {
                image.RecordForeignAcquire(commands);
            }
        });
    }

    /// <summary>
    /// Moves the dmabuf images back to the foreign queue and clears the list. Call it after the flush
    /// that <see cref="AcquireForeignImages"/> prepared.
    /// </summary>
    public void ReleaseForeignImages()
    {
        _thread.Assert();
        if (ForeignThisFrame.Count == 0)
        {
            return;
        }

        _device.SubmitImmediate(this, static (renderer, commands) =>
        {
            foreach (var image in renderer.ForeignThisFrame)
            {
                image.RecordForeignRelease(commands);
            }
        });
        ForeignThisFrame.Clear();
    }

    IRenderDevice? IRenderer.Device => Device;

    /// <summary>
    /// Creates a render stack with a new <see cref="SkiaVulkanRenderer"/> and an allocator on the same
    /// device. The renderer is disposed when the allocator cannot be made.
    /// </summary>
    /// <param name="renderNodePath">The DRM render node to open, for example <c>/dev/dri/renderD128</c>.</param>
    /// <returns>The stack, which owns the renderer and the allocator.</returns>
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

    /// <summary>
    /// Gets the Ganesh context. The renderer owns it.
    /// </summary>
    public GRContext Context => _context;

    internal SkiaVulkanSync Sync => _sync;

    /// <summary>
    /// Gets a value indicating whether a pass can run backdrop effects. It is always <see
    /// langword="true"/> on this renderer.
    /// </summary>
    public bool SupportsBackdropEffects => true;

    internal SkiaBackdropCopy BackdropCopy => _backdropCopy;

    internal SKImage? BackdropImage(Extent2D extent)
    {
        if (_backdropCopyImage is { } cached && _backdropCopy.Matches(extent))
        {
            return cached;
        }

        ReleaseBackdropCopy();
        _backdropCopy.Create(extent);
        _backdropCopyBackend = SkiaCensus.Track(new GRBackendTexture((int)extent.Width, (int)extent.Height, new GRVkImageInfo
        {
            Image = _backdropCopy.Image.Handle,
            ImageTiling = VkImageTilingOptimal,
            ImageLayout = VkImageLayoutShaderReadOnly,
            Format = VkFormatR16G16B16A16Sfloat,
            ImageUsageFlags = VkUsageRenderTarget,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = _device.QueueFamily,
        }));
        var wrapped = SKImage.FromTexture(_context, _backdropCopyBackend, GRSurfaceOrigin.TopLeft, SKColorType.RgbaF16, SKAlphaType.Premul);
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
                ? SkiaVulkanDmabufTexture.TryImport(this, attributes)
                : null;
        }

        return new SkiaVulkanShmTexture(this, buffer);
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
        return SkiaColorLut.Create(lut);
    }

    /// <summary>
    /// Begins a pass that draws into a dmabuf or a CPU buffer. The renderer caches one Skia surface per
    /// target until the buffer is destroyed. An abandoned context drops every cached target and throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="target">A BGRA dmabuf or a 4-byte memory buffer to draw into.</param>
    /// <param name="options">The fences and the output color description of the pass.</param>
    /// <returns>The renderer's single pass object. The caller must submit it before the next pass begins.</returns>
    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (_context.IsAbandoned)
        {
            DropTargets();
            throw new InvalidOperationException("The Ganesh context is abandoned. The cached targets were dropped.");
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

    private SkiaVulkanTarget ImportTarget(IBuffer target)
    {
        var entry = SkiaVulkanTarget.Create(_device, _context, target);
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
    /// Frees every cached target, abandons the Ganesh context and disposes the Vulkan device. It waits
    /// for the device to idle first.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        DropTargets();
        ReleaseBackdropCopy();
        ColorTransforms.Dispose();
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
