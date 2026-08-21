using Basin.Diagnostics;
using Basin.Render.Gl;
using NImpeller;
using Silk.NET.OpenGLES;

namespace Basin.Render.Impeller;

internal sealed unsafe class ImpellerGlDmabufTexture : IImpellerGlTexture
{
    private readonly ImpellerGlRenderer _renderer;
    private readonly Mesa.Egl.EglImage _image;
    private readonly IntPtr _texture;

    private ImpellerGlDmabufTexture(ImpellerGlRenderer renderer, Mesa.Egl.EglImage image, IntPtr texture, in DmabufAttributes attributes)
    {
        _renderer = renderer;
        _image = image;
        _texture = texture;
        Attributes = attributes;
        Width = attributes.Width;
        Height = attributes.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public DmabufAttributes Attributes { get; }

    internal bool SampledThisPass { get; set; }

    internal static ImpellerGlDmabufTexture? TryImport(ImpellerGlRenderer renderer, in DmabufAttributes attributes)
    {
        var device = renderer.Device;
        var image = device.ImportDmabufImage(attributes);
        if (image is null)
        {
            return null;
        }

        device.ClearErrors();
        var gl = device.Gl;
        var textureId = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, textureId);
        device.BindImageToTexture2D(image);
        if (gl.GetError() != GLEnum.NoError)
        {
            gl.DeleteTexture(textureId);
            image.Dispose();
            return null;
        }

        var descriptor = new ImpellerTextureDescriptor
        {
            Pixel_format = ImpellerPixelFormat.kImpellerPixelFormatRGBA8888,
            Size = new ImpellerISize { Width = attributes.Width, Height = attributes.Height },
            Mip_count = 1,
        };
        var texture = UnsafeNativeMethods.ImpellerTextureCreateWithOpenGLTextureHandleNewRaw(
            renderer.ContextRaw, &descriptor, textureId);
        if (texture == IntPtr.Zero)
        {
            gl.DeleteTexture(textureId);
            image.Dispose();
            return null;
        }

        BasinCounters.Track();
        return new ImpellerGlDmabufTexture(renderer, image, texture, attributes);
    }

    public bool Acquire(out IntPtr texture)
    {
        texture = _texture;
        return true;
    }

    public void Dispose()
    {
        UnsafeNativeMethods.ImpellerTextureRelease(_texture);
        BasinCounters.Untrack();
        _image.Dispose();
    }
}
