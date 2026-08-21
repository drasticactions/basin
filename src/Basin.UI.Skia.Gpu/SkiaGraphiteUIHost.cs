using Basin.Capabilities;
using Pixman;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.UI.Skia;

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

    public UITargetKind Produces => _allocator is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

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

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

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

    public void Pump()
    {
    }

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
