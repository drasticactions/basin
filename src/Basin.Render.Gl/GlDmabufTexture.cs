using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Represents a dmabuf that is imported as a 2D texture through an EGL image. The texture reads the dmabuf directly, so it needs no upload.
/// </summary>
public sealed unsafe class GlDmabufTexture : ITexture
{
    private readonly GlDevice _device;
    private readonly EglImage _image;

    private GlDmabufTexture(GlDevice device, EglImage image, uint textureId, in DmabufAttributes attributes)
    {
        _device = device;
        _image = image;
        TextureId = textureId;
        Attributes = attributes;
        Width = attributes.Width;
        Height = attributes.Height;
        HasAlpha = attributes.Format.HasAlpha();
    }

    /// <summary>
    /// Gets the dmabuf that the texture was imported from. The imported buffer owns its fds.
    /// </summary>
    public DmabufAttributes Attributes { get; }

    internal bool SampledThisPass { get; set; }

    /// <summary>
    /// Imports a dmabuf as a 2D texture.
    /// </summary>
    /// <param name="device">The device to import on.</param>
    /// <param name="attributes">The dmabuf to import. Its fds stay owned by the caller.</param>
    /// <returns>The texture, which the caller disposes, or <see langword="null"/> when EGL or GL rejects the dmabuf.</returns>
    public static GlDmabufTexture? TryImport(GlDevice device, in DmabufAttributes attributes)
    {
        var image = device.ImportDmabufImage(attributes);
        if (image is null)
        {
            return null;
        }

        device.ClearErrors();
        var textureId = device.Gl.GenTexture();
        device.Gl.BindTexture(TextureTarget.Texture2D, textureId);
        device.BindImageToTexture2D(image);
        var error = device.Gl.GetError();
        if (error != GLEnum.NoError)
        {
            device.Gl.DeleteTexture(textureId);
            image.Dispose();
            return null;
        }

        return new GlDmabufTexture(device, image, textureId, attributes);
    }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the format of the dmabuf has an alpha channel.
    /// </summary>
    public bool HasAlpha { get; }

    /// <summary>
    /// Gets the name of the 2D texture. The texture owns it.
    /// </summary>
    public uint TextureId { get; }

    /// <summary>
    /// Deletes the GL texture and destroys the EGL image.
    /// </summary>
    public void Dispose()
    {
        _device.Gl.DeleteTexture(TextureId);
        _image.Dispose();
    }
}
