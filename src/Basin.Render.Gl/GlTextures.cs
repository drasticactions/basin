namespace Basin.Render.Gl;

public static class GlTextures
{
    private const uint Texture2D = 0x0DE1;

    public static bool TryOpen(ITexture texture, out GlTextureView view)
    {
        switch (texture)
        {
            case GlDmabufTexture dmabuf:
                view = new GlTextureView(
                    dmabuf.TextureId, Texture2D, dmabuf.Width, dmabuf.Height, true, false, dmabuf.HasAlpha);
                return true;
            case GlYuvTexture yuv when yuv.Acquire(out var converted):
                view = new GlTextureView(converted, Texture2D, yuv.Width, yuv.Height, true, false, false);
                return true;
            case GlShmTexture shm when shm.Acquire(out var name):
                view = new GlTextureView(name, Texture2D, shm.Width, shm.Height, true, false, shm.HasAlpha);
                return true;
            default:
                view = default;
                return false;
        }
    }
}
