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
    private DamageRects _dirty;

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

    public ulong UploadedBytes { get; private set; }

    public void MarkDirty()
    {
        _uploaded = false;
        _dirty.Clear();
        _dirty.Add(0, 0, Width, Height);
    }

    public void MarkDirty(in Box damage)
    {
        var x0 = Math.Clamp(damage.X, 0, Width);
        var y0 = Math.Clamp(damage.Y, 0, Height);
        var x1 = Math.Clamp(damage.X + damage.Width, 0, Width);
        var y1 = Math.Clamp(damage.Y + damage.Height, 0, Height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        if (_uploaded)
        {
            _dirty.Clear();
            _uploaded = false;
        }

        _dirty.Add(x0, y0, x1 - x0, y1 - y0);
    }

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
            var whole = _textureId == 0 ||
                _dirty.Count == 0 ||
                (_dirty.Count == 1 && _dirty[0] == new Box(0, 0, Width, Height));
            if (_textureId == 0)
            {
                _textureId = gl.GenTexture();
            }

            gl.BindTexture(TextureTarget.Texture2D, _textureId);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, view.Stride / 4);
            if (whole)
            {
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
                UploadedBytes += (ulong)Width * (ulong)Height * 4;
            }
            else
            {
                for (var i = 0; i < _dirty.Count; i++)
                {
                    var box = _dirty[i];
                    gl.TexSubImage2D(
                        TextureTarget.Texture2D,
                        0,
                        box.X,
                        box.Y,
                        (uint)box.Width,
                        (uint)box.Height,
                        (PixelFormat)GlBgraExt,
                        PixelType.UnsignedByte,
                        (void*)(view.Data + ((nint)box.Y * view.Stride) + (nint)(box.X * 4)));
                    UploadedBytes += (ulong)box.Width * (ulong)box.Height * 4;
                }
            }

            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            _uploaded = true;
            _dirty.Clear();
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
