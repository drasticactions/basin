using Pixman;

namespace Basin;

public interface IRenderPass
{
    void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null);

    void AddTexture(ITexture texture, in TextureRenderOptions options);

    void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options);

    void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
    }

    void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
    }

    bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options) => false;

    bool Submit();
}
