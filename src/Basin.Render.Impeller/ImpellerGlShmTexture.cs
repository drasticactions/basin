using Basin.Diagnostics;
using Basin.Render.Gl;
using NImpeller;
using Silk.NET.OpenGLES;

namespace Basin.Render.Impeller;

internal sealed unsafe class ImpellerGlShmTexture : IImpellerGlTexture, IRefreshableTexture
{
    private const uint GlBgraExt = 0x80E1;

    private readonly ImpellerGlRenderer _renderer;
    private readonly IBuffer _buffer;
    private uint _textureId;
    private IntPtr _texture;
    private bool _uploaded;

    internal ImpellerGlShmTexture(ImpellerGlRenderer renderer, IBuffer buffer)
    {
        _renderer = renderer;
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public void MarkDirty() => _uploaded = false;

    public bool Acquire(out IntPtr texture)
    {
        if (_uploaded && _texture != IntPtr.Zero)
        {
            texture = _texture;
            return true;
        }

        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            texture = _texture;
            return _uploaded && _texture != IntPtr.Zero;
        }

        try
        {
            var gl = _renderer.Device.Gl;
            var firstUpload = _textureId == 0;
            if (firstUpload)
            {
                _textureId = gl.GenTexture();
            }

            gl.BindTexture(TextureTarget.Texture2D, _textureId);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, view.Stride / 4);
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)GlBgraExt,
                (uint)Width,
                (uint)Height,
                0,
                (PixelFormat)GlBgraExt,
                PixelType.UnsignedByte,
                (void*)view.Data);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            if (firstUpload)
            {
                if (!view.Format.HasAlpha())
                {
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                }

                var descriptor = new ImpellerTextureDescriptor
                {
                    Pixel_format = ImpellerPixelFormat.kImpellerPixelFormatRGBA8888,
                    Size = new ImpellerISize { Width = Width, Height = Height },
                    Mip_count = 1,
                };
                var wrapped = UnsafeNativeMethods.ImpellerTextureCreateWithOpenGLTextureHandleNewRaw(
                    _renderer.ContextRaw, &descriptor, _textureId);
                if (wrapped == IntPtr.Zero)
                {
                    gl.DeleteTexture(_textureId);
                    _textureId = 0;
                    texture = IntPtr.Zero;
                    return false;
                }

                _texture = wrapped;
                BasinCounters.Track();
            }

            _uploaded = true;
            texture = _texture;
            return true;
        }
        finally
        {
            _buffer.EndDataAccess();
        }
    }

    public void Dispose()
    {
        if (_texture != IntPtr.Zero)
        {
            UnsafeNativeMethods.ImpellerTextureRelease(_texture);
            BasinCounters.Untrack();
            _texture = IntPtr.Zero;
            _textureId = 0;
        }
        else if (_textureId != 0)
        {
            _renderer.Device.Gl.DeleteTexture(_textureId);
            _textureId = 0;
        }
    }
}
