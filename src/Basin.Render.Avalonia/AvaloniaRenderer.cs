using Avalonia.Skia;
using Basin.Diagnostics;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Render.Avalonia;

public sealed class AvaloniaRenderer : IRenderer
{
    private readonly SkiaRenderer _raster = new();
    private readonly SKPaint _layerPaint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private SKCanvas? _canvas;
    private GRContext? _lastContext;
    private int _saveCount;
    private bool _lost;
    private AvaloniaEglImport? _eglImport;
    private readonly List<(uint Texture, nint Image, int Generation)> _pendingEglReleases = [];

    public int ContextGeneration { get; private set; }

    public AvaloniaEglImport? EglImport => _eglImport;

    public event Action<AvaloniaEglImport>? EglAvailable;

    internal void ScheduleEglRelease(uint texture, nint image, int generation) =>
        _pendingEglReleases.Add((texture, image, generation));

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

    public AvaloniaRenderer()
    {
        _layerPaint = SkiaCensus.Track(new SKPaint());
    }

    public bool IsContextLost => _lost;

    public event Action? ContextReplaced;

    public bool BindFrame(ISkiaSharpApiLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!BindFrame(lease.SkCanvas, lease.GrContext, lease.CurrentOpacity))
        {
            return false;
        }

        if (_eglImport is null && OperatingSystem.IsLinux() && lease.GrContext is not null)
        {
            using var platform = lease.TryLeasePlatformGraphicsApi();
            if (platform?.Context is global::Avalonia.OpenGL.Egl.EglContext egl &&
                AvaloniaEglImport.TryCreate(egl.Display.Handle) is { } import)
            {
                _eglImport = import;
                EglAvailable?.Invoke(import);
            }
        }

        FlushEglReleases();
        return true;
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
            return _eglImport is { } egl && _lastContext is { } context && egl.Formats.Contains(attributes.Format)
                ? AvaloniaDmabufTexture.TryImport(this, egl, context, attributes)
                : null;
        }

        return _raster.ImportTexture(buffer);
    }

    public DrmFormatSet DmabufTextureFormats => _eglImport?.Formats ?? DrmFormatSet.Empty;

    public ColorTransformCapability ColorTransform => _raster.ColorTransform;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms) =>
        _raster.CompilePixelShader(source, uniforms);

    public IColorLut? ImportLut(ColorLut3D lut) => _raster.ImportLut(lut);

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();
        if (target is not AvaloniaFrameTarget)
        {
            throw new InvalidOperationException("This renderer draws into the bound lease; only an AvaloniaFrameTarget names a frame.");
        }

        if (_canvas is null)
        {
            throw new InvalidOperationException("No lease is bound; call BindFrame before rendering.");
        }

        RenderFences.WaitSyncFile(options.WaitFenceFd);
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
