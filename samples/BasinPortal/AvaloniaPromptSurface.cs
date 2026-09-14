using Basin;
using Basin.Portal.Client;
using Basin.Capabilities;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using Pixman;
using Wayland;

namespace BasinPortal;

internal sealed class AvaloniaPromptSurface : IUISurfaceObserver, IDisposable
{
    private readonly AvaloniaPromptHost _host;
    private readonly ManagerSurface _surface;
    private readonly ClientLoop _loop;
    private readonly int _scale;
    private int _width;
    private int _height;
    private bool _disposed;

    public AvaloniaPromptSurface(AvaloniaPromptHost host, WlCompositor compositor, WlShm shm, ZwlrLayerShellV1 layerShell,
        ClientOutput output, IUISurface surface, bool cover, ClientLoop loop)
    {
        _host = host;
        Surface = surface;
        _loop = loop;
        _scale = Math.Max(1, (int)Math.Round(output.Scale));
        var size = surface.Size;
        _width = Math.Max(1, size.Width);
        _height = Math.Max(1, size.Height);
        _surface = new ManagerSurface(compositor, shm, layerShell, output.Proxy, ZwlrLayerShellV1.Layer.Overlay, "basin-portal-prompt");
        _surface.SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive);
        if (cover)
        {
            _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        }
        else
        {
            _surface.SetSize(_width, _height);
        }

        surface.AddObserver(this);
        _surface.Configured += OnConfigured;
        _surface.CommitInitial();
    }

    public IUISurface Surface { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public void PointerEnter(double x, double y) => Surface.NotifyPointerEnter(x, y);

    public void PointerMove(double x, double y) => Surface.NotifyPointerMotion(Now(), x, y);

    public void PointerButton(uint button, bool pressed) => Surface.NotifyPointerButton(Now(), button, pressed);

    public void PointerAxis(double dx, double dy) => Surface.NotifyPointerAxis(Now(), dx, dy);

    public void PointerLeave() => Surface.NotifyPointerLeave();

    public void Key(uint key, bool pressed) => Surface.NotifyKey(Now(), key, pressed);

    public void Modifiers(uint depressed, uint latched, uint locked, uint group) =>
        Surface.NotifyModifiers(depressed, latched, locked, group);

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage) => Publish();

    public void OnSurfaceDestroyed(IUISurface surface) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Surface.RemoveObserver(this);
        _surface.Configured -= OnConfigured;
        _host.Remove(this);
        _surface.Dispose();
    }

    private static uint Now() => (uint)Environment.TickCount64;

    private void OnConfigured()
    {
        if (_disposed)
        {
            return;
        }

        var size = _surface.ConfiguredSize;
        if (size.Width > 0 && size.Height > 0)
        {
            _width = size.Width;
            _height = size.Height;
        }

        Surface.Configure(_width, _height, _scale);
        Publish();
    }

    private void Publish()
    {
        if (_disposed || !_surface.IsConfigured || !Surface.TryAcquire(out var frame))
        {
            return;
        }

        using (frame)
        {
            if (frame.Buffer is not { } buffer)
            {
                return;
            }

            var pixels = _surface.Prepare(_width, _height, _scale);
            if (pixels == 0 || !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
            {
                return;
            }

            try
            {
                Copy(buffer, view);
            }
            finally
            {
                buffer.EndDataAccess();
            }

            _surface.Commit();
            _loop.RequestFlush();
        }
    }

    private void Copy(IBuffer buffer, BufferDataView view)
    {
        var target = _surface.Bytes;
        var targetStride = _width * _scale * 4;
        var rows = Math.Min(buffer.Height, _height * _scale);
        var rowBytes = Math.Min(buffer.Width, _width * _scale) * 4;
        unsafe
        {
            for (var y = 0; y < rows; y++)
            {
                var source = new ReadOnlySpan<byte>((byte*)view.Data + ((long)y * view.Stride), rowBytes);
                source.CopyTo(target.Slice(y * targetStride, rowBytes));
            }
        }
    }
}
