using Basin.Render.Gl;
using Pixman;
using SkiaSharp;
using Silk.NET.OpenGLES;

namespace Basin.Render.Skia;

internal sealed unsafe class SkiaGlRenderPass : IRenderPass
{
    private readonly SkiaGlRenderer _renderer;
    private readonly SKPaint _paint;
    private IBuffer? _target;
    private SkiaGlRenderer.TargetEntry? _entry;
    private int _signalFenceFd = -1;

    private readonly List<SkiaGlDmabufTexture> _sampled = [];

    internal SkiaGlRenderPass(SkiaGlRenderer renderer, SKPaint paint)
    {
        _renderer = renderer;
        _paint = paint;
    }

    internal void Begin(IBuffer target, SkiaGlRenderer.TargetEntry entry, int signalFenceFd)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Rect(_entry!.Canvas, _paint, color, box, clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (texture is SkiaGlDmabufTexture { SampledThisPass: false } dmabuf)
        {
            dmabuf.SampledThisPass = true;
            _sampled.Add(dmabuf);
        }

        SkiaDraw.Texture(_entry!.Canvas, _paint, (ISkiaTexture)texture, options);
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

    public bool Submit()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        var entry = _entry!;
        _target = null;
        _entry = null;

        _renderer.Context.Flush(submit: true, synchronous: false);

        var gl = _renderer.Device.Gl;
        if (entry.Native.IsCpuReadback)
        {
            ClearSampled();
            entry.Native.ReadInto(_renderer.Device, target);
        }
        else
        {
            PublishFence(entry, gl);
        }

        if (_signalFenceFd >= 0)
        {
            gl.Finish();
            RenderFences.SignalSyncobjFd(_renderer.Device.DrmFd, _signalFenceFd);
            _signalFenceFd = -1;
        }

        return true;
    }

    private void PublishFence(SkiaGlRenderer.TargetEntry entry, GL gl)
    {
        var fence = _renderer.Device.ExportFence();
        if (fence < 0)
        {
            gl.Flush();
            ClearSampled();
            return;
        }

        _renderer.ReplaceCompletionFence(RenderFences.DuplicateFence(fence));

        RenderFences.PublishFenceTo(entry.Native.Attributes, forWrite: true, fence);
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
            RenderFences.PublishFenceTo(texture.Native.Attributes, forWrite: false, fence);
        }

        _sampled.Clear();
        RenderFences.CloseFence(fence);
    }

    private void ClearSampled()
    {
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
        }

        _sampled.Clear();
    }
}
