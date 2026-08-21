using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

public sealed unsafe class GlShmTexture : ITexture, IRefreshableTexture
{
    private const uint GlBgraExt = 0x80E1;

    private readonly GlDevice _device;
    private readonly IBuffer _buffer;
    private uint _textureId;
    private bool _uploaded;

    public GlShmTexture(GlDevice device, IBuffer buffer)
    {
        _device = device;
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public bool HasAlpha { get; private set; }

    public void MarkDirty() => _uploaded = false;

    public bool Acquire(out uint textureId)
    {
        if (_uploaded)
        {
            textureId = _textureId;
            return true;
        }

        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            textureId = 0;
            return _uploaded;
        }

        try
        {
            HasAlpha = view.Format.HasAlpha();
            var gl = _device.Gl;
            if (_textureId == 0)
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
            _uploaded = true;
            textureId = _textureId;
            return true;
        }
        finally
        {
            _buffer.EndDataAccess();
        }
    }

    public void Dispose()
    {
        if (_textureId != 0)
        {
            _device.Gl.DeleteTexture(_textureId);
            _textureId = 0;
        }
    }
}
