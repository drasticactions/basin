using Basin.WindowManager;
using Dinghy.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class ManagerSurface : IDisposable
{
    private readonly WlCompositor _compositor;
    private readonly ShmSlots _slots;
    private readonly ZwlrLayerSurfaceV1 _layerSurface;
    private bool _disposed;

    internal ManagerSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput output,
        ZwlrLayerShellV1.Layer layer,
        string scope)
    {
        _compositor = compositor;
        Surface = compositor.CreateSurface();
        _slots = new ShmSlots(shm);
        _layerSurface = layerShell.GetLayerSurface(Surface, output, layer, scope);
        _layerSurface.Configure += (_, e) =>
        {
            _layerSurface.AckConfigure(e.Serial);
            ConfiguredSize = new Size((int)e.Width, (int)e.Height);
            IsConfigured = true;
            Configured?.Invoke();
        };
        _layerSurface.SetExclusiveZone(-1);
        _layerSurface.SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity.None);
    }

    public WlSurface Surface { get; }

    public uint SurfaceId => Surface.Id;

    public bool IsConfigured { get; private set; }

    public Size ConfiguredSize { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Scale { get; private set; }

    public event Action? Configured;

    public void SetAnchor(ZwlrLayerSurfaceV1.Anchor anchor) => _layerSurface.SetAnchor(anchor);

    public void SetSize(int width, int height) => _layerSurface.SetSize((uint)width, (uint)height);

    public void SetMargin(int top, int left) => _layerSurface.SetMargin(top, 0, 0, left);

    public void CommitInitial() => Surface.Commit();

    public nint Prepare(int width, int height, int scale)
    {
        scale = Math.Max(scale, 1);
        var pixels = _slots.Prepare(width * scale, height * scale, width * scale * 4);
        if (pixels == 0)
        {
            return 0;
        }

        Width = width;
        Height = height;
        Scale = scale;
        Surface.SetBufferScale(scale);
        _slots.CurrentBytes().Clear();
        return pixels;
    }

    public Span<byte> Bytes => _slots.CurrentBytes();

    public SKSurface? CreateCanvas(nint pixels) => SKSurface.Create(
        new SKImageInfo(Width * Scale, Height * Scale, SKColorType.Bgra8888, SKAlphaType.Premul),
        pixels,
        Width * Scale * 4);

    public void SetInputRegion(Rect area)
    {
        var region = _compositor.CreateRegion();
        if (!area.IsEmpty)
        {
            region.Add(area.X, area.Y, area.Width, area.Height);
        }

        Surface.SetInputRegion(region);
        region.Destroy();
    }

    public void SetInputRegion(IReadOnlyList<Rect> areas)
    {
        var region = _compositor.CreateRegion();
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (!area.IsEmpty)
            {
                region.Add(area.X, area.Y, area.Width, area.Height);
            }
        }

        Surface.SetInputRegion(region);
        region.Destroy();
    }

    public void Commit()
    {
        if (_slots.CurrentBuffer is not { } buffer)
        {
            return;
        }

        Surface.Attach(buffer, 0, 0);
        Surface.DamageBuffer(0, 0, Width * Scale, Height * Scale);
        Surface.Commit();
        _slots.MarkAttached();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _slots.Dispose();

        if (!_layerSurface.IsDestroyed)
        {
            _layerSurface.Destroy();
        }

        if (!Surface.IsDestroyed)
        {
            Surface.Destroy();
        }
    }
}
