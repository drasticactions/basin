using Basin.Capabilities;
using Pixman;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// The Skia chrome host on the Graphite context of a Skia Graphite renderer. It draws <see cref="SkiaVulkanUISurface"/> instances through the shared recorder. Use it on the thread that created it.
/// </summary>
public sealed class SkiaGraphiteUIHost : IUISurfaceObserver, IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly VulkanDevice _device;
    private readonly SKGraphiteContext _context;
    private readonly SKGraphiteRecorder _recorder;
    private readonly IAllocator? _allocator;
    private readonly List<SkiaVulkanUISurface> _surfaces = [];
    private ulong[]? _modifiers;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaGraphiteUIHost"/> class. The host owns none of its arguments.
    /// </summary>
    /// <param name="device">The Vulkan device of the renderer.</param>
    /// <param name="context">The Graphite context that each frame submits to.</param>
    /// <param name="recorder">The Graphite recorder that the surfaces record into.</param>
    /// <param name="allocator">The allocator of the dmabufs. When it is <see langword="null"/>, the host produces memory buffers only.</param>
    public SkiaGraphiteUIHost(
        VulkanDevice device,
        SKGraphiteContext context,
        SKGraphiteRecorder recorder,
        IAllocator? allocator = null)
    {
        _device = device;
        _context = context;
        _recorder = recorder;
        _allocator = allocator;
    }

    /// <summary>
    /// Gets <see cref="ISkiaUISurface"/>, which every surface of the host implements.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    /// <summary>
    /// Gets <see cref="UITargetKind.Dmabuf"/> when the host has an allocator, otherwise <see cref="UITargetKind.Memory"/>.
    /// </summary>
    public UITargetKind Produces => _allocator is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

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
    /// Creates a configured <see cref="SkiaVulkanUISurface"/> and tracks it until it is disposed.
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
            buffer => new GraphiteDrawTarget(SkiaGraphiteTarget.Create(_device, _recorder, buffer)),
            Flush,
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
    /// Does nothing. The host watches its surfaces only for their destruction.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region.</param>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    /// <summary>
    /// Stops tracking a surface that the host made.
    /// </summary>
    /// <param name="surface">The surface that was disposed.</param>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (surface is SkiaVulkanUISurface vulkanSurface)
        {
            _surfaces.Remove(vulkanSurface);
        }
    }

    private void Flush()
    {
        var recording = _recorder.Snap();
        if (recording is null)
        {
            return;
        }

        SkiaCensus.Track(recording);
        try
        {
            _ = _context.InsertRecording(recording);
            _ = _context.Submit(new SKGraphiteSubmitInfo());
        }
        finally
        {
            SkiaCensus.Release(recording);
        }
    }

    private ulong[] RenderableModifiers() => _modifiers ??=
        [.. _device.RenderableFormats.ModifiersOf(DrmFormat.Argb8888).Where(m => m != DrmFormatSet.ModifierInvalid)];

    /// <summary>
    /// Does nothing. The caller draws each surface.
    /// </summary>
    public void Pump()
    {
    }

    /// <summary>
    /// Disposes every surface that the host made and that is still live. The Graphite context stays with the renderer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var surface in _surfaces.ToArray())
        {
            surface.Dispose();
        }

        _surfaces.Clear();
    }
}
