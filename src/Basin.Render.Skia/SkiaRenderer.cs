using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// The Skia raster renderer. It draws into CPU-accessible buffers and runs on the compositor thread
/// only.
/// </summary>
public sealed class SkiaRenderer : IRenderer
{
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly SkiaRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly SKColorFilter _opaqueAlpha;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    /// <summary>
    /// Creates a render stack with a new Skia raster renderer and no allocator.
    /// </summary>
    /// <returns>The stack, which owns the renderer.</returns>
    public static RenderStack CreateStack() => new(new SkiaRenderer(), null);

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaRenderer"/> class. The instance belongs to the
    /// thread that creates it.
    /// </summary>
    public SkiaRenderer()
    {
        _paint = SkiaCensus.Track(new SKPaint());
        _opaqueAlpha = SkiaCensus.Track(SkiaDraw.CreateOpaqueAlpha());
        _pass = new SkiaRenderPass(_paint, _opaqueAlpha);
    }

    /// <summary>
    /// Wraps a buffer as a texture. The texture reads the buffer's pixels on each draw and never
    /// returns <see langword="null"/>.
    /// </summary>
    /// <param name="buffer">The client or compositor buffer to sample.</param>
    /// <returns>A new texture that the caller owns.</returns>
    public ITexture? ImportTexture(IBuffer buffer)
    {
        _thread.Assert();
        return new SkiaTexture(buffer);
    }

    /// <inheritdoc/>
    public IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.All;

    /// <summary>
    /// Gets a value indicating whether the renderer can draw into half-float buffers. It is always <see
    /// langword="true"/>.
    /// </summary>
    public bool RendersHalfFloat => true;

    /// <summary>
    /// Gets the color transform this renderer supports. It is always <see
    /// cref="ColorTransformCapability.Lut3D"/>.
    /// </summary>
    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    /// <summary>
    /// Gets the fence precision of a submitted pass. It is always <see
    /// cref="RenderFencePrecision.None"/>, because the CPU finishes drawing before submit returns.
    /// </summary>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.None;

    /// <summary>
    /// Compiles the SkSL variant of a pixel shader.
    /// </summary>
    /// <param name="source">The shader source. Only its SkSL text is read.</param>
    /// <param name="uniforms">The uniform layout of the shader.</param>
    /// <returns>The compiled shader, or <see langword="null"/> when the source has no SkSL.</returns>
    /// <exception cref="InvalidOperationException">The SkSL does not compile.</exception>
    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut);
    }

    /// <summary>
    /// Starts a pass that draws into a canvas the caller owns. The pass does not begin or end data
    /// access on the target buffer.
    /// </summary>
    /// <param name="target">The buffer that the canvas draws into.</param>
    /// <param name="canvas">The canvas to draw with.</param>
    /// <returns>The renderer's single pass object. It is valid until the pass submits.</returns>
    public IRenderPass BeginCanvasPass(IBuffer target, SKCanvas canvas) => BeginCanvasPass(target, canvas, null);

    /// <summary>
    /// Starts a pass that draws into a canvas the caller owns, and converts the colors that the compositor draws through a table.
    /// The pass does not begin or end data access on the target buffer.
    /// </summary>
    /// <param name="target">The buffer that the canvas draws into.</param>
    /// <param name="canvas">The canvas to draw with.</param>
    /// <param name="solidColorLut">A table from <see cref="ImportLut"/> for <see cref="RenderPassOptions.SolidColorLut"/>, or <see langword="null"/> to draw those colors as they are.</param>
    /// <returns>The renderer's single pass object. It is valid until the pass submits.</returns>
    public IRenderPass BeginCanvasPass(IBuffer target, SKCanvas canvas, IColorLut? solidColorLut)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(canvas);
        _pass.Begin(target, canvas, endsDataAccess: false, (solidColorLut as SkiaColorLut)?.Filter);
        return _pass;
    }

    /// <summary>
    /// Waits on the options' fence, then starts a pass that draws into the buffer's CPU pixels. The
    /// renderer caches one surface per buffer until the buffer is destroyed.
    /// </summary>
    /// <param name="target">A buffer with CPU-accessible pixels in a format that <see cref="TryImageInfo"/> maps.</param>
    /// <param name="options">The pass options. The wait fence and <see cref="RenderPassOptions.SolidColorLut"/> are read.</param>
    /// <returns>The renderer's single pass object. It is valid until the pass submits.</returns>
    /// <exception cref="InvalidOperationException">The buffer has no CPU pixels, or Skia rejects its layout.</exception>
    /// <exception cref="NotSupportedException">The buffer's format has no Skia mapping.</exception>
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

        _pass.Begin(target, entry.Canvas, endsDataAccess: true, (options.SolidColorLut as SkiaColorLut)?.Filter);
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

    /// <summary>
    /// Frees every cached target surface and the shared paint objects.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        foreach (var entry in _targets.Values)
        {
            SkiaCensus.Release(entry.Surface);
        }

        _targets.Clear();
        SkiaCensus.Release(_paint);
        SkiaCensus.Release(_opaqueAlpha);
    }

    /// <summary>
    /// Maps a DRM format onto the Skia color type and alpha type that draw into it.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="format">The DRM format of the pixels.</param>
    /// <param name="info">The image description, or the default value when the format has no mapping.</param>
    /// <returns><see langword="true"/> when Skia can draw in the format.</returns>
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
            case DrmFormat.Abgr16161616f:
                info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul);
                return true;
            case DrmFormat.Xbgr16161616f:
                info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Opaque);
                return true;
            case DrmFormat.Rgb565:
                info = new SKImageInfo(width, height, SKColorType.Rgb565, SKAlphaType.Opaque);
                return true;
            default:
                info = default;
                return false;
        }
    }

    private sealed record TargetEntry(nint Data, SKSurface Surface, SKCanvas Canvas);
}
