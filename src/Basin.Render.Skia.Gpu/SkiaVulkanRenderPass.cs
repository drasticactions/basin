using Basin.Render.Skia;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Pixman;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed unsafe class SkiaVulkanRenderPass : IRenderPass
{
    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    private readonly SkiaVulkanRenderer _renderer;
    private int _acquired;
    private readonly SKPaint _paint;
    private IBuffer? _target;
    private SkiaVulkanTarget? _entry;
    private int _signalFenceFd = -1;

    internal SkiaVulkanRenderPass(SkiaVulkanRenderer renderer, SKPaint paint)
    {
        _renderer = renderer;
        _paint = paint;
    }

    private ImageDescription? _outputColor;

    internal void Begin(IBuffer target, SkiaVulkanTarget entry, int signalFenceFd, ImageDescription? outputColor)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _outputColor = outputColor;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Rect(_entry!.Canvas, _paint, _renderer.ColorTransforms.ConvertRect(color, _outputColor), box, clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Texture(
            _entry!.Canvas, _paint, (ISkiaTexture)texture, options,
            options.Lut is null && options.Shader is null ? _renderer.ColorTransforms.TransformFor(options.ColorDescription, _outputColor) : null);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Mesh(_entry!.Canvas, _paint, (ISkiaTexture?)texture, vertices, options);
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Shader(_entry!.Canvas, _paint, shader, options);
    }

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (effect is not IVulkanBackdropEffect vulkanEffect)
        {
            throw new ArgumentException("effect does not belong to this renderer");
        }

        if (bounds.IsEmpty)
        {
            return;
        }

        var entry = _entry!;
        AcquireForeign();
        _renderer.Context.Flush(submit: true, synchronous: false);

        var extent = new Extent2D((uint)_target.Width, (uint)_target.Height);
        var image = _renderer.BackdropImage(extent);
        if (!_renderer.BackdropCopy.Run(
                vulkanEffect, entry.Image, entry.BackdropView(), ImageLayout.ColorAttachmentOptimal,
                extent, bounds, key, out var source)
            || image is null)
        {
            return;
        }

        var canvas = entry.Canvas;
        var srcRect = SKRect.Create(source.X, source.Y, source.Width, source.Height);
        var dstRect = SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _paint.BlendMode = SKBlendMode.Src;
        _paint.SetColor(new SKColorF(1f, 1f, 1f, 1f), null);
        if (clip is null)
        {
            canvas.DrawImage(image, srcRect, dstRect, NearestSampling, _paint);
        }
        else
        {
            foreach (var band in RegionRects.Of(clip))
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                canvas.DrawImage(image, srcRect, dstRect, NearestSampling, _paint);
                canvas.Restore();
            }
        }

        _paint.BlendMode = SKBlendMode.SrcOver;
    }

    private void AcquireForeign()
    {
        if (_renderer.ForeignThisFrame.Count <= _acquired)
        {
            return;
        }

        _renderer.Device.SubmitImmediate((_renderer, _acquired), static (state, commands) =>
        {
            var foreign = state.Item1.ForeignThisFrame;
            for (var i = state.Item2; i < foreign.Count; i++)
            {
                foreign[i].RecordForeignAcquire(commands);
            }
        });
        _acquired = _renderer.ForeignThisFrame.Count;
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "SkiaVulkanSubmit", forgiving: true);
        try
        {
            return SubmitCore();
        }
        finally
        {
            AllocationScope.End();
        }
    }

    private bool SubmitCore()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        var entry = _entry!;
        _target = null;
        _entry = null;

        AcquireForeign();
        _acquired = 0;

        _renderer.Context.Flush(submit: true, synchronous: false);

        _renderer.Sync.DrainAndSignal(_signalFenceFd);
        _signalFenceFd = -1;

        if (_renderer.ForeignThisFrame.Count > 0)
        {
            _renderer.Device.SubmitImmediate(_renderer, static (renderer, commands) =>
            {
                foreach (var image in renderer.ForeignThisFrame)
                {
                    image.RecordForeignRelease(commands);
                }
            });
            _renderer.ForeignThisFrame.Clear();
        }

        if (entry.IsCpuReadback)
        {
            entry.ReadInto(target);
        }

        return true;
    }
}
