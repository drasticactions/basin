namespace Basin.Render.Gl;

/// <summary>
/// Opens the textures of a <see cref="GlRenderer"/> for code that draws with its own GL calls on the same context.
/// </summary>
public static class GlTextures
{
    private const uint Texture2D = 0x0DE1;

    /// <summary>
    /// Gets the GL texture of a texture. It uploads pending shared-memory content and converts a YUV texture first.
    /// </summary>
    /// <param name="texture">The texture to open.</param>
    /// <param name="view">The handles of the texture, or the default value on failure.</param>
    /// <returns><see langword="false"/> when the texture is not a GL texture or a shared-memory upload failed.</returns>
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
