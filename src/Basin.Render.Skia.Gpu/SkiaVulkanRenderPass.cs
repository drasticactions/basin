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
    private readonly SkiaVulkanRenderer _renderer;
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

        if (_renderer.ForeignThisFrame.Count > 0)
        {
            _renderer.Device.SubmitImmediate(_renderer, static (renderer, commands) =>
            {
                foreach (var image in renderer.ForeignThisFrame)
                {
                    image.RecordForeignAcquire(commands);
                }
            });
        }

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
