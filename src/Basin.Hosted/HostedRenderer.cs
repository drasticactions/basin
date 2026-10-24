using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Hosted;

/// <summary>
/// A renderer that draws into a Skia canvas that the host view lends for each frame. It uses the raster Skia row for every draw.
/// Only the thread that constructed it can use it.
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
    private readonly List<(uint Texture, nint Image, int Generation)> _pendingEglReleases = [];

    /// <summary>
    /// Gets the count of times that <see cref="BindFrame"/> received a different <see cref="GRContext"/>.
    /// </summary>
    public int ContextGeneration { get; private set; }

    /// <summary>
    /// Gets the EGL dmabuf import. It is <see langword="null"/> until <see cref="TryEnableEgl"/> succeeds.
    /// </summary>
    public HostedEglImport? EglImport => _eglImport;

    /// <summary>
    /// Occurs once, when <see cref="TryEnableEgl"/> creates the EGL import.
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

    internal void ScheduleEglRelease(uint texture, nint image, int generation) =>
        _pendingEglReleases.Add((texture, image, generation));

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
    /// </summary>
    /// <param name="eglDisplay">The native EGL display of the host toolkit.</param>
    /// <returns><see langword="true"/> when an EGL import is in use, including one from a previous call.</returns>
    public bool TryEnableEgl(nint eglDisplay)
    {
        _thread.Assert();
        if (_eglImport is not null || !HostedEglImport.IsSupported)
        {
            return _eglImport is not null;
        }

        if (HostedEglImport.TryCreate(eglDisplay) is not { } import)
        {
            return false;
        }

        _eglImport = import;
        EglAvailable?.Invoke(import);
        return true;
    }

    private void FlushEglReleases()
    {
        if (_eglImport is null || _pendingEglReleases.Count == 0)
        {
            return;
        }

        foreach (var (texture, image, generation) in _pendingEglReleases)
        {
            if (generation == ContextGeneration)
            {
                _eglImport.Destroy(texture, image);
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
            throw new InvalidOperationException("The previous frame was not unbound.");
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
    /// Imports a buffer as a texture. A dmabuf goes to <see cref="DmabufRenderer"/> or to the EGL import. Any other buffer goes to the raster Skia row.
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

            return _eglImport is { } egl && _lastContext is { } context && egl.Formats.Contains(attributes.Format)
                ? HostedDmabufTexture.TryImport(this, egl, context, attributes)
                : null;
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
    /// Gets <see cref="RenderFencePrecision.None"/>, because the host owns the submission and the renderer exports no fence.
    /// </summary>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    /// <inheritdoc/>
    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) =>
        _raster.CompilePixelShader(source, uniforms);

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut) => _raster.ImportLut(lut);

    /// <summary>
    /// Begins a pass on the bound canvas. It waits on the wait fence on the CPU and scales the canvas by the reciprocal of the target scale.
    /// </summary>
    /// <param name="target">The <see cref="HostedFrameTarget"/> of the frame.</param>
    /// <param name="options">The pass options. The wait fence and <see cref="RenderPassOptions.SolidColorLut"/> are read here.</param>
    /// <returns>A raster Skia pass over the leased canvas.</returns>
    /// <exception cref="InvalidOperationException">The target is not a <see cref="HostedFrameTarget"/>, or no frame is bound.</exception>
    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (target is not HostedFrameTarget frame)
        {
            throw new InvalidOperationException("This renderer draws into the bound lease; only an HostedFrameTarget names a frame.");
        }

        if (_canvas is null)
        {
            throw new InvalidOperationException("No lease is bound; call BindFrame before rendering.");
        }

        RenderFences.WaitSyncFile(options.WaitFenceFd);
        if (Math.Abs(frame.Scale - 1.0) > double.Epsilon)
        {
            _canvas.Save();
            _canvas.Scale((float)(1.0 / frame.Scale));
        }

        return _raster.BeginCanvasPass(target, _canvas, options.SolidColorLut);
    }

    /// <summary>
    /// Unbinds a bound frame and releases the Skia resources of the renderer.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        UnbindFrame();
        SkiaCensus.Release(_layerPaint);
        _raster.Dispose();
    }
}
