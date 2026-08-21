using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

public sealed class SkiaRenderer : IRenderer
{
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly SkiaRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    public static RenderStack CreateStack() => new(new SkiaRenderer(), null);

    public SkiaRenderer()
    {
        _paint = SkiaCensus.Track(new SKPaint());
        _pass = new SkiaRenderPass(_paint);
    }

    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        return new SkiaTexture(buffer);
    }

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut);
    }

    public IRenderPass BeginCanvasPass(IBuffer target, SKCanvas canvas)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(canvas);
        _pass.Begin(target, canvas, endsDataAccess: false);
        return _pass;
    }

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();

        RenderFences.WaitSyncFile(options.WaitFenceFd);
        if (!target.BeginDataAccess(BufferDataAccess.Read | BufferDataAccess.Write, out var view))
        {
            throw new InvalidOperationException("Render target has no CPU-accessible pixels.");
        }

        if (!_targets.TryGetValue(target, out var entry) || entry.Data != view.Data)
        {
            entry = ImportTarget(target, view);
        }

        _pass.Begin(target, entry.Canvas);
        return _pass;
    }

    private TargetEntry ImportTarget(IBuffer target, in BufferDataView view)
    {
        if (_targets.Remove(target, out var stale))
        {
            SkiaCensus.Release(stale.Surface);
        }

        if (!TryImageInfo(target.Width, target.Height, view.Format, out var info))
        {
            target.EndDataAccess();
            throw new NotSupportedException($"Format 0x{(uint)view.Format:X8} is not supported by this renderer.");
        }

        var surface = SKSurface.Create(info, view.Data, view.Stride);
        if (surface is null)
        {
            target.EndDataAccess();
            throw new InvalidOperationException("Skia rejected the render target's pixel layout.");
        }

        var entry = new TargetEntry(view.Data, SkiaCensus.Track(surface), surface.Canvas);
        _targets[target] = entry;
        target.Destroyed += () =>
        {
            if (_targets.Remove(target, out var dead))
            {
                SkiaCensus.Release(dead.Surface);
            }
        };
        return entry;
    }

    public void Dispose()
    {
        _thread.Assert();
        foreach (var entry in _targets.Values)
        {
            SkiaCensus.Release(entry.Surface);
        }

        _targets.Clear();
        SkiaCensus.Release(_paint);
    }

    public static bool TryImageInfo(int width, int height, DrmFormat format, out SKImageInfo info)
    {
        switch (format)
        {
            case DrmFormat.Argb8888:
                info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                return true;
            case DrmFormat.Xrgb8888:
                info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                return true;
            case DrmFormat.Abgr8888:
                info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                return true;
            case DrmFormat.Xbgr8888:
                info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
                return true;
            case DrmFormat.Argb2101010:
                info = new SKImageInfo(width, height, SKColorType.Bgra1010102, SKAlphaType.Premul);
                return true;
            case DrmFormat.Xrgb2101010:
                info = new SKImageInfo(width, height, SKColorType.Bgr101010x, SKAlphaType.Opaque);
                return true;
            case DrmFormat.Abgr2101010:
                info = new SKImageInfo(width, height, SKColorType.Rgba1010102, SKAlphaType.Premul);
                return true;
            case DrmFormat.Xbgr2101010:
                info = new SKImageInfo(width, height, SKColorType.Rgb101010x, SKAlphaType.Opaque);
                return true;
            default:
                info = default;
                return false;
        }
    }

    private sealed record TargetEntry(nint Data, SKSurface Surface, SKCanvas Canvas);
}
