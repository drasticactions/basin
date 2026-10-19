using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Hosted;

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

    public int ContextGeneration { get; private set; }

    public HostedEglImport? EglImport => _eglImport;

    public event Action<HostedEglImport>? EglAvailable;

    private IRenderer? _dmabufRenderer;

    public IRenderer? DmabufRenderer => _dmabufRenderer;

    public event Action<DrmFormatSet, string>? DmabufAvailable;

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

    public HostedRenderer()
    {
        _layerPaint = SkiaCensus.Track(new SKPaint());
    }

    public bool IsContextLost => _lost;

    public event Action? ContextReplaced;

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

    public void NotifyContextLost()
    {
        _thread.Assert();
        _lost = true;
        _lastContext = null;
    }

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

    public IReadOnlyList<DrmFormat> ShmTextureFormats => _raster.ShmTextureFormats;

    public DrmFormatSet DmabufTextureFormats =>
        _dmabufRenderer?.DmabufTextureFormats ?? _eglImport?.Formats ?? DrmFormatSet.Empty;

    public ColorTransformCapability ColorTransform => _raster.ColorTransform;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) =>
        _raster.CompilePixelShader(source, uniforms);

    public IColorLut? ImportLut(ColorLut3D lut) => _raster.ImportLut(lut);

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

        return _raster.BeginCanvasPass(target, _canvas);
    }

    public void Dispose()
    {
        _thread.Assert();
        UnbindFrame();
        SkiaCensus.Release(_layerPaint);
        _raster.Dispose();
    }
}
