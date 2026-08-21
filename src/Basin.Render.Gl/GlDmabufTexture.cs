using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

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

    public DmabufAttributes Attributes { get; }

    internal bool SampledThisPass { get; set; }

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

    public int Width { get; }

    public int Height { get; }

    public bool HasAlpha { get; }

    public uint TextureId { get; }

    public void Dispose()
    {
        _device.Gl.DeleteTexture(TextureId);
        _image.Dispose();
    }
}
