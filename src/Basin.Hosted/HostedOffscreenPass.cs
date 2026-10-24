using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.Hosted;

internal sealed class HostedOffscreenPass : IRenderPass
{
    private IRenderPass? _inner;
    private SKSurface? _surface;
    private IBuffer? _target;

    public IRenderPass Begin(IRenderPass inner, SKSurface surface, IBuffer target)
    {
        _inner = inner;
        _surface = surface;
        _target = target;
        return this;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null) => Inner.AddRect(color, box, clip);

    public void Clear(in RenderColor color) => Inner.Clear(color);

    public void AddTexture(ITexture texture, in TextureRenderOptions options) => Inner.AddTexture(texture, options);

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options) =>
        Inner.AddMesh(texture, vertices, options);

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null) =>
        Inner.AddBackdropEffect(effect, bounds, clip, key);

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key) =>
        Inner.AddBackdropEffect(effect, bounds, extent, clip, key);

    public void AddDeformedBackdropEffect(
        IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key, ReadOnlySpan<MeshVertex> mesh) =>
        Inner.AddDeformedBackdropEffect(effect, bounds, extent, clip, key, mesh);

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options) => Inner.AddShader(shader, options);

    public bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options) =>
        Inner.AddFrameFilter(filter, source, options);

    public bool Submit()
    {
        var inner = Inner;
        var surface = _surface!;
        var target = _target!;
        _inner = null;
        _surface = null;
        _target = null;
        if (!inner.Submit())
        {
            return false;
        }

        surface.Flush();
        if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        try
        {
            return SkiaRenderer.TryImageInfo(target.Width, target.Height, view.Format, out var info) &&
                surface.ReadPixels(info, view.Data, view.Stride, 0, 0);
        }
        finally
        {
            target.EndDataAccess();
        }
    }

    private IRenderPass Inner => _inner ?? throw new InvalidOperationException("No offscreen pass is open. Call BeginBufferPass first.");
}
