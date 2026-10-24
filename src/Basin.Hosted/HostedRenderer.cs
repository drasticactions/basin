using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

/// <summary>
/// A renderer that draws into a Skia canvas that the host view lends for each frame, through the raster Skia row.
/// Outside a frame it also draws into other buffers, for example capture targets. Only the thread that constructed it can use it.
/// </summary>
public sealed class HostedRenderer : IRenderer
{
    private readonly SkiaRenderer _raster = new();
    private readonly SKPaint _layerPaint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SKCanvas? _canvas;
    private GRContext? _lastContext;
    private int _saveCount;
    private bool _lost;
    private HostedEglImport? _eglImport;
    private readonly List<(uint Texture, nint Image, int Generation, HostedEglImport Import)> _pendingEglReleases = [];
    private readonly HostedOffscreenPass _offscreenPass = new();
    private GRContext? _offscreenContext;
    private IHostedContextBinding? _offscreenBinding;
    private int _bindingDepth;
    private bool _bindingWarned;
    private bool _passEntered;
    private SKBitmap? _loadBitmap;
    private SKPaint? _loadPaint;
    private SKSurface? _offscreenSurface;
    private int _offscreenWidth;
    private int _offscreenHeight;

    /// <summary>
    /// Gets the count of times that <see cref="BindFrame"/> received a different <see cref="GRContext"/>.
    /// </summary>
    public int ContextGeneration { get; private set; }

    /// <summary>
    /// Gets the EGL dmabuf import. It is <see langword="null"/> until <see cref="TryEnableEgl"/> succeeds.
    /// </summary>
    public HostedEglImport? EglImport => _eglImport;

    /// <summary>
    /// Occurs when <see cref="TryEnableEgl"/> creates an EGL import: the first one, and each one on a new EGL display.
    /// </summary>
    public event Action<HostedEglImport>? EglAvailable;

    private IRenderer? _dmabufRenderer;

    /// <summary>
    /// Gets the renderer that receives every dmabuf import. It is <see langword="null"/> unless <see cref="UseDmabufRenderer"/> set one.
    /// </summary>
    public IRenderer? DmabufRenderer => _dmabufRenderer;

    /// <summary>
    /// Occurs when <see cref="UseDmabufRenderer"/> accepts a renderer, with its dmabuf formats and its render node path.
    /// </summary>
    public event Action<DrmFormatSet, string>? DmabufAvailable;

    /// <summary>
    /// Sends every dmabuf import to a renderer that shares the host's <see cref="GRContext"/>, and raises <see cref="DmabufAvailable"/>.
    /// </summary>
    /// <param name="renderer">The renderer that imports dmabufs. Its textures must draw on the leased canvas.</param>
    /// <param name="renderNodePath">The render node of the renderer's device.</param>
    /// <returns><see langword="true"/> when the renderer is in use. It is <see langword="false"/> when the renderer has no dmabuf formats or another renderer is in use.</returns>
    public bool UseDmabufRenderer(IRenderer renderer, string renderNodePath)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(renderNodePath);
        if (_dmabufRenderer is not null)
        {
            return ReferenceEquals(_dmabufRenderer, renderer);
        }

        if (renderer.DmabufTextureFormats.Count == 0)
        {
            return false;
        }

        _dmabufRenderer = renderer;
        DmabufAvailable?.Invoke(renderer.DmabufTextureFormats, renderNodePath);
        return true;
    }

    /// <summary>
    /// Stops sending dmabuf imports to <paramref name="renderer"/>. Another renderer does nothing.
    /// </summary>
    /// <param name="renderer">The renderer that <see cref="UseDmabufRenderer"/> accepted.</param>
    public void StopDmabufRenderer(IRenderer renderer)
    {
        _thread.Assert();
        if (ReferenceEquals(_dmabufRenderer, renderer))
        {
            _dmabufRenderer = null;
        }
    }

    internal void ScheduleEglRelease(uint texture, nint image, int generation, HostedEglImport import) =>
        _pendingEglReleases.Add((texture, image, generation, import));

    internal bool ToolkitLendsContext { get; set; }

    internal GRContext? DrawingGpuContext => _canvas is not null ? _lastContext : _passEntered ? _offscreenContext : null;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedRenderer"/> class on the calling thread.
    /// </summary>
    public HostedRenderer()
    {
        _layerPaint = SkiaCensus.Track(new SKPaint());
    }

    /// <summary>
    /// Gets a value indicating whether the GPU context is lost. While it is lost, <see cref="ImportTexture"/> returns <see langword="null"/>.
    /// </summary>
    public bool IsContextLost => _lost;

    /// <summary>
    /// Occurs in <see cref="BindFrame"/> when the lease carries a different <see cref="GRContext"/> than the previous frame.
    /// </summary>
    public event Action? ContextReplaced;

    /// <summary>
    /// Enables dmabuf import through the host's EGL display and raises <see cref="EglAvailable"/> on success.
    /// A call with a different display replaces the import. Textures from the previous display then import again on their next draw.
    /// </summary>
    /// <param name="eglDisplay">The native EGL display of the host toolkit.</param>
    /// <returns><see langword="true"/> when an EGL import on this display is in use, including one from a previous call.</returns>
    public bool TryEnableEgl(nint eglDisplay)
    {
        _thread.Assert();
        if (!HostedEglImport.IsSupported)
        {
            return false;
        }

        if (_eglImport is { } current && current.DisplayHandle == eglDisplay)
        {
            return true;
        }

        var previous = _eglImport;
        if (HostedEglImport.TryCreate(eglDisplay) is not { } import)
        {
            if (previous is not null)
            {
                _eglImport = null;
                Log.Warn($"dmabuf: no import on the new EGL display 0x{eglDisplay:X}, dmabuf textures are not drawn");
            }

            return false;
        }

        _eglImport = import;
        if (previous is not null)
        {
            Log.Info($"dmabuf: EGL display changed from 0x{previous.DisplayHandle:X} to 0x{eglDisplay:X}, textures import again");
        }

        EglAvailable?.Invoke(import);
        return true;
    }

    /// <summary>
    /// Gets the GPU context that buffer passes outside a frame draw with. It is <see langword="null"/> until <see cref="UseOffscreenContext"/> sets one.
    /// </summary>
    public GRContext? OffscreenContext => _offscreenContext;

    /// <summary>
    /// Gets the binding that makes <see cref="OffscreenContext"/> current outside a frame. It is <see langword="null"/> when that context is always current.
    /// </summary>
    public IHostedContextBinding? OffscreenBinding => _offscreenBinding;

    /// <summary>
    /// Sets the GPU context that buffer passes outside a frame draw with, and makes it the context of dmabuf imports.
    /// A pass on that context reads the pixels back into the target buffer.
    /// Without a binding, the context must be current on this thread for every later call. With a binding, the renderer enters it around each pass, import and release outside a frame.
    /// </summary>
    /// <param name="context">The GPU context, or <see langword="null"/> to draw those passes on the raster row again.</param>
    /// <param name="binding">The binding that makes the context current, or <see langword="null"/> when the context stays current.</param>
    public void UseOffscreenContext(GRContext? context, IHostedContextBinding? binding = null)
    {
        _thread.Assert();
        if (ReferenceEquals(context, _offscreenContext))
        {
            if (context is not null && _bindingDepth == 0)
            {
                _offscreenBinding = binding;
            }

            return;
        }

        LeaveOpenPass();
        if (_offscreenSurface is not null && EnterOffscreenContext())
        {
            try
            {
                DropOffscreenSurface();
            }
            finally
            {
                ExitOffscreenContext();
            }
        }

        DropOffscreenSurface();
        _offscreenContext = context;
        _offscreenBinding = context is null ? null : binding;
        _bindingWarned = false;
        if (context is not null && !ReferenceEquals(context, _lastContext))
        {
            _lastContext = context;
            _lost = false;
            ContextGeneration++;
            ContextReplaced?.Invoke();
        }
    }

    private bool EnterOffscreenContext()
    {
        if (_offscreenBinding is not { } binding)
        {
            return true;
        }

        if (_bindingDepth > 0)
        {
            _bindingDepth++;
            return true;
        }

        if (!binding.Enter())
        {
            if (!_bindingWarned)
            {
                _bindingWarned = true;
                Log.Warn($"capture: GPU context did not become current, passes outside a frame draw on the raster row");
            }

            return false;
        }

        _bindingWarned = false;
        _bindingDepth = 1;
        return true;
    }

    private void ExitOffscreenContext()
    {
        if (_bindingDepth == 0)
        {
            return;
        }

        if (--_bindingDepth == 0)
        {
            _offscreenBinding?.Exit();
        }
    }

    internal void EndOffscreenPass()
    {
        if (_passEntered)
        {
            _passEntered = false;
            ExitOffscreenContext();
        }
    }

    private void LeaveOpenPass()
    {
        EndOffscreenPass();
        while (_bindingDepth > 0)
        {
            ExitOffscreenContext();
        }
    }

    private void FlushStaleEglReleases()
    {
        for (var i = _pendingEglReleases.Count - 1; i >= 0; i--)
        {
            var (_, image, generation, import) = _pendingEglReleases[i];
            if (generation != ContextGeneration || !ReferenceEquals(import, _eglImport))
            {
                import.DestroyImage(image);
                _pendingEglReleases.RemoveAt(i);
            }
        }
    }

    private void LoadTarget(SKSurface surface, IBuffer target)
    {
        var canvas = surface.Canvas;
        if (!target.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }

        try
        {
            _loadBitmap ??= SkiaCensus.Track(new SKBitmap());
            _loadPaint ??= SkiaCensus.Track(new SKPaint { BlendMode = SKBlendMode.Src });
            if (!SkiaRenderer.TryImageInfo(target.Width, target.Height, view.Format, out var info) ||
                !_loadBitmap.InstallPixels(info, view.Data, view.Stride))
            {
                canvas.Clear(SKColors.Transparent);
                return;
            }

            canvas.DrawBitmap(_loadBitmap, 0, 0, SKSamplingOptions.Default, _loadPaint);
            surface.Flush();
        }
        finally
        {
            _loadBitmap?.Reset();
            target.EndDataAccess();
        }
    }

    private void DropOffscreenSurface()
    {
        SkiaCensus.Release(_offscreenSurface);
        _offscreenSurface = null;
    }

    private void FlushEglReleases()
    {
        if (_pendingEglReleases.Count == 0)
        {
            return;
        }

        foreach (var (texture, image, generation, import) in _pendingEglReleases)
        {
            if (generation == ContextGeneration && ReferenceEquals(import, _eglImport))
            {
                import.Destroy(texture, image);
            }
            else
            {
                import.DestroyImage(image);
            }
        }

        _pendingEglReleases.Clear();
    }

    /// <summary>
    /// Binds the leased canvas for one frame and saves its state. Each successful call must be paired with one <see cref="UnbindFrame"/>.
    /// </summary>
    /// <param name="canvas">The canvas that the host lends.</param>
    /// <param name="context">The GPU context of the canvas, or <see langword="null"/> for a raster canvas.</param>
    /// <param name="opacity">The opacity of the whole frame. A value below 1 draws through a save layer.</param>
    /// <returns><see langword="false"/> when the context is abandoned. The renderer then counts as lost.</returns>
    /// <exception cref="InvalidOperationException">The previous frame is still bound.</exception>
    public bool BindFrame(SKCanvas canvas, GRContext? context, double opacity = 1.0)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(canvas);
        if (_canvas is not null)
        {
            throw new InvalidOperationException("The previous frame is still bound. Call UnbindFrame before binding a new frame.");
        }

        if (context is not null && context.IsAbandoned)
        {
            _lost = true;
            return false;
        }

        if (!ReferenceEquals(context, _lastContext))
        {
            _lastContext = context;
            ContextGeneration++;
            ContextReplaced?.Invoke();
        }

        _lost = false;
        if (opacity < 1.0)
        {
            _layerPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255.0, 0.0, 255.0));
            _saveCount = canvas.SaveLayer(_layerPaint);
        }
        else
        {
            _saveCount = canvas.Save();
        }

        _canvas = canvas;
        FlushEglReleases();
        return true;
    }

    /// <summary>
    /// Restores the canvas state that <see cref="BindFrame"/> saved and releases the canvas. A call with no bound frame does nothing.
    /// </summary>
    public void UnbindFrame()
    {
        _thread.Assert();
        if (_canvas is null)
        {
            return;
        }

        _canvas.RestoreToCount(_saveCount);
        _canvas = null;
    }

    /// <summary>
    /// Marks the GPU context as lost. Imports return <see langword="null"/> until the next successful <see cref="BindFrame"/>.
    /// </summary>
    public void NotifyContextLost()
    {
        _thread.Assert();
        _lost = true;
        _lastContext = null;
    }

    /// <summary>
    /// Imports a buffer as a texture. A dmabuf goes to <see cref="DmabufRenderer"/>, or to the EGL import while a frame is bound or <see cref="OffscreenContext"/> is set.
    /// With an EGL import and no GPU context, a dmabuf texture waits and imports on its first draw on a GPU context. Any other buffer, and a dmabuf with no EGL import, goes to the raster Skia row.
    /// </summary>
    /// <param name="buffer">The client buffer.</param>
    /// <returns>The texture, or <see langword="null"/> when the context is lost or no import path accepts the dmabuf format.</returns>
    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        if (_lost)
        {
            return null;
        }

        if (buffer.TryGetDmabuf(out var attributes))
        {
            if (_dmabufRenderer is { } delegated)
            {
                return delegated.DmabufTextureFormats.Contains(attributes.Format)
                    ? delegated.ImportTexture(buffer)
                    : null;
            }

            if (_canvas is null && _offscreenContext is null)
            {
                return _eglImport is { } pending && pending.Formats.Contains(attributes.Format)
                    ? HostedDmabufTexture.Deferred(this, buffer, pending, attributes)
                    : _raster.ImportTexture(buffer);
            }

            if (_eglImport is not { } egl || _lastContext is not { } context || !egl.Formats.Contains(attributes.Format))
            {
                return null;
            }

            if (_canvas is not null)
            {
                return HostedDmabufTexture.TryImport(this, buffer, egl, context, attributes);
            }

            if (!EnterOffscreenContext())
            {
                return HostedDmabufTexture.Deferred(this, buffer, egl, attributes);
            }

            try
            {
                return HostedDmabufTexture.TryImport(this, buffer, egl, context, attributes);
            }
            finally
            {
                ExitOffscreenContext();
            }
        }

        return _raster.ImportTexture(buffer);
    }

    /// <inheritdoc/>
    public IReadOnlyList<DrmFormat> ShmTextureFormats => _raster.ShmTextureFormats;

    /// <summary>
    /// Gets the dmabuf formats of <see cref="DmabufRenderer"/>, else of the EGL import, else an empty set.
    /// </summary>
    public DrmFormatSet DmabufTextureFormats =>
        _dmabufRenderer?.DmabufTextureFormats ?? _eglImport?.Formats ?? DrmFormatSet.Empty;

    /// <inheritdoc/>
    public ColorTransformCapability ColorTransform => _raster.ColorTransform;

    /// <summary>
    /// Gets a value indicating whether a pass outside a frame can draw into a half-float buffer. It follows the renderer that takes those passes.
    /// </summary>
    public bool RendersHalfFloat => _dmabufRenderer?.RendersHalfFloat ?? _raster.RendersHalfFloat;

    /// <summary>
    /// Gets <see cref="RenderFencePrecision.None"/>, because the host owns the submission and the renderer exports no fence.
    /// </summary>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    /// <inheritdoc/>
    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) =>
        _raster.CompilePixelShader(source, uniforms);

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut) => _raster.ImportLut(lut);

    /// <summary>
    /// Begins a pass on the bound canvas, or into another buffer. It waits on the wait fence on the CPU.
    /// A frame pass scales the canvas by the reciprocal of the target scale.
    /// A pass into another buffer goes to <see cref="DmabufRenderer"/>, else to <see cref="OffscreenContext"/>, else to the raster Skia row.
    /// </summary>
    /// <param name="target">The <see cref="HostedFrameTarget"/> of the frame, or a buffer with CPU pixels.</param>
    /// <param name="options">The pass options. The wait fence and <see cref="RenderPassOptions.SolidColorLut"/> are read here.</param>
    /// <returns>A raster Skia pass over the leased canvas, or a pass into the buffer.</returns>
    /// <exception cref="InvalidOperationException">The target is a <see cref="HostedFrameTarget"/> and no frame is bound, or the target has no CPU pixels.</exception>
    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (target is not HostedFrameTarget frame)
        {
            return BeginOffscreenPass(target, options);
        }

        if (_canvas is null)
        {
            throw new InvalidOperationException("No lease is bound. Call BindFrame before rendering.");
        }

        RenderFences.WaitSyncFile(options.WaitFenceFd);
        if (Math.Abs(frame.Scale - 1.0) > double.Epsilon)
        {
            _canvas.Save();
            _canvas.Scale((float)(1.0 / frame.Scale));
        }

        return _raster.BeginCanvasPass(target, _canvas, options.SolidColorLut);
    }

    private IRenderPass BeginOffscreenPass(IBuffer target, in RenderPassOptions options)
    {
        if (_dmabufRenderer is { } delegated)
        {
            return delegated.BeginBufferPass(target, options);
        }

        if (_offscreenContext is not { } context)
        {
            return _raster.BeginBufferPass(target, options);
        }

        EndOffscreenPass();
        if (!EnterOffscreenContext())
        {
            return _raster.BeginBufferPass(target, options);
        }

        if (context.IsAbandoned)
        {
            ExitOffscreenContext();
            return _raster.BeginBufferPass(target, options);
        }

        _passEntered = true;
        try
        {
            RenderFences.WaitSyncFile(options.WaitFenceFd);
            if (_offscreenSurface is not { } surface || _offscreenWidth != target.Width || _offscreenHeight != target.Height)
            {
                DropOffscreenSurface();
                var info = new SKImageInfo(target.Width, target.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                surface = SKSurface.Create(context, false, info)
                    ?? throw new InvalidOperationException($"Skia could not create a {target.Width}x{target.Height} GPU surface. The target is too large for the device.");
                _offscreenSurface = SkiaCensus.Track(surface);
                _offscreenWidth = target.Width;
                _offscreenHeight = target.Height;
            }

            var canvas = surface.Canvas;
            canvas.RestoreToCount(1);
            canvas.ResetMatrix();
            LoadTarget(surface, target);
            FlushEglReleases();
            return _offscreenPass.Begin(this, _raster.BeginCanvasPass(target, canvas, options.SolidColorLut), surface, target);
        }
        catch
        {
            EndOffscreenPass();
            throw;
        }
    }

    /// <summary>
    /// Unbinds a bound frame and releases the Skia resources of the renderer.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        UnbindFrame();
        LeaveOpenPass();
        if (_offscreenContext is null)
        {
            FlushStaleEglReleases();
        }
        else if (EnterOffscreenContext())
        {
            try
            {
                FlushEglReleases();
                DropOffscreenSurface();
            }
            finally
            {
                ExitOffscreenContext();
            }
        }

        DropOffscreenSurface();
        _offscreenContext = null;
        _offscreenBinding = null;
        SkiaCensus.Release(_layerPaint);
        SkiaCensus.Release(_loadBitmap);
        SkiaCensus.Release(_loadPaint);
        _loadBitmap = null;
        _loadPaint = null;
        _raster.Dispose();
    }
}
