using Pixman;

namespace Basin;

public interface IRenderPass
{
    void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null);

    void Clear(in RenderColor color);

    void AddTexture(ITexture texture, in TextureRenderOptions options);

    void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options);

    void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
    }

    void AddBackdropEffect(IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key) =>
        AddBackdropEffect(effect, bounds, clip, key);

    void AddDeformedBackdropEffect(
        IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key, ReadOnlySpan<MeshVertex> mesh) =>
        AddBackdropEffect(effect, bounds, extent, clip, key);

    void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
    }

    bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options) => false;

    bool Submit();
}
