using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace Basin.WindowManager.Skia;

/// <summary>
/// A wlr layer surface that the manager draws with Skia over a pair of shm buffers. It starts with exclusive zone -1 and no keyboard interactivity.
/// </summary>
public sealed class ManagerSurface : IDisposable
{
    private readonly WlCompositor _compositor;
    private readonly ShmSlots _slots;
    private readonly ZwlrLayerSurfaceV1 _layerSurface;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagerSurface"/> class. It creates the surface and its layer surface, which stays unmapped until <see cref="CommitInitial"/> and a configure.
    /// </summary>
    /// <param name="compositor">The compositor global that creates the surface and regions.</param>
    /// <param name="shm">The shm global for the buffers.</param>
    /// <param name="layerShell">The layer shell that gives the surface its role.</param>
    /// <param name="output">The output to show the surface on.</param>
    /// <param name="layer">The layer to place the surface in.</param>
    /// <param name="scope">The namespace that the compositor sees, for example "bar".</param>
    public ManagerSurface(
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

    /// <summary>
    /// Gets the surface under the layer surface. This object owns it, and a manager can pass it to the blur client.
    /// </summary>
    public WlSurface Surface { get; }

    /// <summary>
    /// Gets the protocol id of <see cref="Surface"/>, which pointer events name.
    /// </summary>
    public uint SurfaceId => Surface.Id;

    /// <summary>
    /// Gets a value indicating whether the first configure arrived. Do not attach a buffer before it does.
    /// </summary>
    public bool IsConfigured { get; private set; }

    /// <summary>
    /// Gets the size in logical pixels from the last configure. A zero component lets the client choose it.
    /// </summary>
    public Size ConfiguredSize { get; private set; }

    /// <summary>
    /// Gets the logical width of the last prepared buffer.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Gets the logical height of the last prepared buffer.
    /// </summary>
    public int Height { get; private set; }

    /// <summary>
    /// Gets the buffer scale of the last prepared buffer, or zero before the first prepare.
    /// </summary>
    public int Scale { get; private set; }

    /// <summary>
    /// Occurs after each configure, which this object acknowledges first.
    /// </summary>
    public event Action? Configured;

    /// <summary>
    /// Sets the edges of the output that the surface is anchored to. It applies on the next commit.
    /// </summary>
    /// <param name="anchor">The anchor edges.</param>
    public void SetAnchor(ZwlrLayerSurfaceV1.Anchor anchor) => _layerSurface.SetAnchor(anchor);

    /// <summary>
    /// Sets the exclusive zone. It applies on the next commit.
    /// </summary>
    /// <param name="zone">The zone in logical pixels. Zero moves for other zones, and -1 ignores them.</param>
    public void SetExclusiveZone(int zone) => _layerSurface.SetExclusiveZone(zone);

    /// <summary>
    /// Moves the surface to another layer. It applies on the next commit.
    /// </summary>
    /// <param name="layer">The new layer.</param>
    public void SetLayer(ZwlrLayerShellV1.Layer layer) => _layerSurface.SetLayer(layer);

    /// <summary>
    /// Sets whether the surface can take keyboard focus. It applies on the next commit.
    /// </summary>
    /// <param name="interactivity">The keyboard interactivity.</param>
    public void SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivity interactivity) =>
        _layerSurface.SetKeyboardInteractivity(interactivity);

    /// <summary>
    /// Sets the size that the surface asks for. It applies on the next commit.
    /// </summary>
    /// <param name="width">The width in logical pixels. Zero lets the compositor choose, with left and right anchors.</param>
    /// <param name="height">The height in logical pixels. Zero lets the compositor choose, with top and bottom anchors.</param>
    public void SetSize(int width, int height) => _layerSurface.SetSize((uint)width, (uint)height);

    /// <summary>
    /// Sets the top and left margins and sets the right and bottom margins to zero. It applies on the next commit.
    /// </summary>
    /// <param name="top">The top margin in logical pixels.</param>
    /// <param name="left">The left margin in logical pixels.</param>
    public void SetMargin(int top, int left) => _layerSurface.SetMargin(top, 0, 0, left);

    /// <summary>
    /// Commits with no buffer, which asks the compositor for the first configure.
    /// </summary>
    public void CommitInitial() => Surface.Commit();

    /// <summary>
    /// Selects a free buffer for a logical size at a scale, sets the buffer scale and clears the buffer to transparent.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    /// <param name="scale">The buffer scale. A value under 1 becomes 1.</param>
    /// <returns>The address of the first pixel, or zero when the size is not positive or the memory cannot be mapped.</returns>
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

    /// <summary>
    /// Gets the pixels of the prepared buffer, for a painter that writes raw pixels. It is empty before the first prepare.
    /// </summary>
    public Span<byte> Bytes => _slots.CurrentBytes();

    /// <summary>
    /// Creates a premultiplied BGRA Skia surface over the prepared buffer, in buffer pixels.
    /// </summary>
    /// <param name="pixels">The address that <see cref="Prepare"/> returned.</param>
    /// <returns>The Skia surface, which the caller disposes, or <see langword="null"/> when Skia cannot create it.</returns>
    public SKSurface? CreateCanvas(nint pixels) => SKSurface.Create(
        new SKImageInfo(Width * Scale, Height * Scale, SKColorType.Bgra8888, SKAlphaType.Premul),
        pixels,
        Width * Scale * 4);

    /// <summary>
    /// Sets the input region to one rectangle. An empty rectangle lets all input pass through. It applies on the next commit.
    /// </summary>
    /// <param name="area">The rectangle in surface-local logical pixels.</param>
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

    /// <summary>
    /// Sets the input region to the union of rectangles. Empty rectangles are skipped. It applies on the next commit.
    /// </summary>
    /// <param name="areas">The rectangles in surface-local logical pixels.</param>
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

    /// <summary>
    /// Attaches the prepared buffer, damages all of it and commits. It does nothing before the first prepare.
    /// </summary>
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

    /// <summary>
    /// Frees the buffers and destroys the layer surface and then the surface. A second call does nothing.
    /// </summary>
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
