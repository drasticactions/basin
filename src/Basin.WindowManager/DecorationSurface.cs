using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// A surface under a window decoration, for example a titlebar, a frame or a shadow. It owns the <see cref="WlSurface"/>, the <see cref="WmDecoration"/> and a pair of shm buffers.
/// </summary>
public sealed class DecorationSurface : IDisposable
{
    private readonly WlCompositor _compositor;
    private readonly WlSurface _surface;
    private readonly WmDecoration _decoration;
    private readonly ShmSlots _slots;
    private readonly OutputScales _scales;
    private readonly HashSet<WlOutput> _entered = [];
    private WmBackgroundEffects? _effects;
    private Box _blurred;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecorationSurface"/> class. It creates a surface and gives it the decoration role on the window.
    /// </summary>
    /// <param name="window">The window to decorate.</param>
    /// <param name="compositor">The compositor global that creates the surface and regions.</param>
    /// <param name="shm">The shm global for the buffers.</param>
    /// <param name="scales">The output scales that <see cref="ScaleFor"/> reads.</param>
    /// <param name="above"><see langword="true"/> to place the decoration above the window, <see langword="false"/> to place it below.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DecorationSurface(WmWindow window, WlCompositor compositor, WlShm shm, OutputScales scales, bool above)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(shm);
        ArgumentNullException.ThrowIfNull(scales);
        _compositor = compositor;
        _scales = scales;
        _surface = compositor.CreateSurface();
        _surface.Enter += (_, e) =>
        {
            if (e.Output is not null)
            {
                _entered.Add(e.Output);
            }
        };
        _surface.Leave += (_, e) =>
        {
            if (e.Output is not null)
            {
                _entered.Remove(e.Output);
            }
        };
        _decoration = above ? window.CreateDecorationAbove(_surface) : window.CreateDecorationBelow(_surface);
        _slots = new ShmSlots(shm);
    }

    /// <summary>
    /// Gets the surface that carries the decoration role. This object owns it.
    /// </summary>
    public WlSurface Surface => _surface;

    /// <summary>
    /// Gets the protocol id of <see cref="Surface"/>, which pointer events name.
    /// </summary>
    public uint SurfaceId => _surface.Id;

    /// <summary>
    /// Gets a value indicating whether the last commit attached a buffer. <see cref="Unmap"/> makes it false.
    /// </summary>
    public bool Mapped { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Prepare"/> made a buffer.
    /// </summary>
    public bool HasBuffer => _slots.CurrentBuffer is not null;

    /// <summary>
    /// Gets the width in buffer pixels of the last buffer that <see cref="Prepare"/> made.
    /// </summary>
    public int BufferWidth { get; private set; }

    /// <summary>
    /// Gets the height in buffer pixels of the last buffer that <see cref="Prepare"/> made.
    /// </summary>
    public int BufferHeight { get; private set; }

    /// <summary>
    /// Gets the largest scale of the outputs that the surface entered.
    /// </summary>
    /// <param name="fallbackOutputName">The registry name of the output to use before the surface entered any.</param>
    /// <returns>The integer buffer scale, at least 1.</returns>
    public int ScaleFor(uint fallbackOutputName)
    {
        if (_entered.Count == 0)
        {
            return _scales.ScaleForName(fallbackOutputName);
        }

        var scale = 1;
        foreach (var output in _entered)
        {
            scale = Math.Max(scale, _scales.ScaleFor(output));
        }

        return scale;
    }

    /// <summary>
    /// Sets the buffer scale for the next commit.
    /// </summary>
    /// <param name="scale">The scale. A value under 1 becomes 1.</param>
    public void SetBufferScale(int scale) => _surface.SetBufferScale(Math.Max(scale, 1));

    /// <summary>
    /// Sets the decoration's offset from the window's top-left corner, as <see cref="WmDecoration.SetOffset(int, int)"/> does.
    /// </summary>
    /// <param name="x">The horizontal offset in logical pixels.</param>
    /// <param name="y">The vertical offset in logical pixels.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SetOffset(int x, int y) => _decoration.SetOffset(x, y);

    /// <summary>
    /// Holds the next commit until render finish, as <see cref="WmDecoration.SyncNextCommit"/> does. The caller must call <see cref="Commit()"/> or <see cref="Unmap"/> before the sequence ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SyncNextCommit() => _decoration.SyncNextCommit();

    /// <summary>
    /// Sets an empty input region, so pointer input passes through the decoration. It applies on the next commit.
    /// </summary>
    public void ClearInputRegion() => SetInputRegion([], []);

    /// <summary>
    /// Sets the input region from rectangles to add and then to subtract. It applies on the next commit.
    /// </summary>
    /// <param name="add">The rectangles to add, in surface-local logical pixels.</param>
    /// <param name="subtract">The rectangles to subtract after the additions.</param>
    public void SetInputRegion(ReadOnlySpan<Rect> add, ReadOnlySpan<Rect> subtract)
    {
        var region = _compositor.CreateRegion();
        foreach (var rect in add)
        {
            region.Add(rect.X, rect.Y, rect.Width, rect.Height);
        }

        foreach (var rect in subtract)
        {
            region.Subtract(rect.X, rect.Y, rect.Width, rect.Height);
        }

        _surface.SetInputRegion(region);
        region.Destroy();
    }

    /// <summary>
    /// Sets the blur region behind the surface. It sends the region only when it differs from the last one, and the surface forgets it on dispose.
    /// </summary>
    /// <param name="effects">The blur client to send through.</param>
    /// <param name="region">The region in surface-local logical pixels. An empty box unsets it.</param>
    /// <returns><see langword="true"/> when the region changed and was sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is <see langword="null"/>.</exception>
    public bool SetBlur(WmBackgroundEffects effects, Box region)
    {
        ArgumentNullException.ThrowIfNull(effects);
        _effects = effects;
        if (region == _blurred)
        {
            return false;
        }

        _blurred = region;
        effects.SetBlurRegion(_surface, region.IsEmpty ? [] : [region]);
        return true;
    }

    /// <summary>
    /// Selects a free ARGB8888 buffer of the given size and clears it to transparent.
    /// </summary>
    /// <param name="bufferWidth">The width in buffer pixels.</param>
    /// <param name="bufferHeight">The height in buffer pixels.</param>
    /// <returns>The address of the first pixel, at four bytes per pixel. It is zero when a size is not positive or the memory cannot be mapped.</returns>
    public nint Prepare(int bufferWidth, int bufferHeight)
    {
        if (bufferWidth <= 0 || bufferHeight <= 0)
        {
            return 0;
        }

        var pixels = _slots.Prepare(bufferWidth, bufferHeight, bufferWidth * 4);
        if (pixels == 0)
        {
            return 0;
        }

        BufferWidth = bufferWidth;
        BufferHeight = bufferHeight;
        _slots.CurrentBytes().Clear();
        return pixels;
    }

    /// <summary>
    /// Gets the pixels of the buffer that <see cref="Prepare"/> selected, for a painter that writes raw pixels. It is empty before the first prepare.
    /// </summary>
    public Span<byte> Bytes => _slots.CurrentBytes();

    /// <summary>
    /// Attaches the prepared buffer, damages all of it and commits through <see cref="WmDecoration.Commit"/>, which clears a <see cref="SyncNextCommit"/> obligation. It does nothing before the first <see cref="Prepare"/>.
    /// </summary>
    public void Commit() => Commit([new Rect(0, 0, BufferWidth, BufferHeight)]);

    /// <summary>
    /// Attaches the prepared buffer, damages the given rectangles and commits through <see cref="WmDecoration.Commit"/>, which clears a <see cref="SyncNextCommit"/> obligation. It does nothing before the first <see cref="Prepare"/>.
    /// </summary>
    /// <param name="damage">The changed rectangles in buffer pixels.</param>
    public void Commit(ReadOnlySpan<Rect> damage)
    {
        if (_slots.CurrentBuffer is not { } buffer)
        {
            return;
        }

        _surface.Attach(buffer, 0, 0);
        foreach (var rect in damage)
        {
            _surface.DamageBuffer(rect.X, rect.Y, rect.Width, rect.Height);
        }

        _decoration.Commit();
        _slots.MarkAttached();
        Mapped = true;
    }

    /// <summary>
    /// Attaches no buffer and commits through <see cref="WmDecoration.Commit"/>, which hides the decoration.
    /// </summary>
    public void Unmap()
    {
        _surface.Attach(null, 0, 0);
        _decoration.Commit();
        Mapped = false;
    }

    /// <summary>
    /// Forgets the blur region, frees the buffers, destroys the decoration role and then the surface. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _effects?.Forget(_surface);
        _slots.Dispose();
        _decoration.Dispose();
        if (!_surface.IsDestroyed)
        {
            _surface.Destroy();
        }
    }
}
