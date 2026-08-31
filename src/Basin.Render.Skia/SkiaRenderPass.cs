using Basin.Diagnostics;
using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaRenderPass : IRenderPass
{
    private readonly SKPaint _paint;
    private IBuffer? _target;
    private SKCanvas? _canvas;
    private bool _endsDataAccess;

    internal SkiaRenderPass(SKPaint paint) => _paint = paint;

    internal void Begin(IBuffer target, SKCanvas canvas) => Begin(target, canvas, endsDataAccess: true);

    internal void Begin(IBuffer target, SKCanvas canvas, bool endsDataAccess)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _canvas = canvas;
        _endsDataAccess = endsDataAccess;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Rect(_canvas!, _paint, color, box, clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Texture(_canvas!, _paint, (ISkiaTexture)texture, options);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Mesh(_canvas!, _paint, (ISkiaTexture?)texture, vertices, options);
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Shader(_canvas!, _paint, shader, options);
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "SkiaSubmit", forgiving: true);
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
        _target = null;
        _canvas = null;

        if (_endsDataAccess)
        {
            target.EndDataAccess();
        }

        return true;
    }
}
