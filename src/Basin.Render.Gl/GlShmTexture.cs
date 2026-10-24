using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Represents a CPU-readable buffer that is uploaded into a 2D texture. Only dirty regions are uploaded again.
/// </summary>
public sealed unsafe class GlShmTexture : ITexture, IRefreshableTexture
{
    private const uint GlBgraExt = 0x80E1;

    private const uint GlRgba = 0x1908;

    private const uint GlRgb = 0x1907;

    private const uint GlRgba8 = 0x8058;

    private const uint GlRgb10A2 = 0x8059;

    private const uint GlRgb565 = 0x8D62;

    private const uint GlRgba16f = 0x881A;

    private const uint GlUnsignedByte = 0x1401;

    private const uint GlHalfFloat = 0x140B;

    private const uint GlUnsigned2101010Rev = 0x8368;

    private const uint GlUnsignedShort565 = 0x8363;

    /// <summary>
    /// Tells whether the texture can upload a memory format.
    /// </summary>
    /// <param name="format">The format of the buffer.</param>
    /// <returns><see langword="true"/> for the 8-bit, 10-bit and half-float RGB formats and RGB565.</returns>
    public static bool Uploads(DrmFormat format) => Layout(format).BytesPerPixel != 0;

    private static (uint Internal, uint Format, uint Type, int BytesPerPixel, bool SwapRedBlue) Layout(DrmFormat format) => format switch
    {
        DrmFormat.Argb8888 or DrmFormat.Xrgb8888 => (GlBgraExt, GlBgraExt, GlUnsignedByte, 4, false),
        DrmFormat.Abgr8888 or DrmFormat.Xbgr8888 => (GlRgba8, GlRgba, GlUnsignedByte, 4, false),
        DrmFormat.Abgr2101010 or DrmFormat.Xbgr2101010 => (GlRgb10A2, GlRgba, GlUnsigned2101010Rev, 4, false),
        DrmFormat.Argb2101010 or DrmFormat.Xrgb2101010 => (GlRgb10A2, GlRgba, GlUnsigned2101010Rev, 4, true),
        DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f => (GlRgba16f, GlRgba, GlHalfFloat, 8, false),
        DrmFormat.Rgb565 => (GlRgb565, GlRgb, GlUnsignedShort565, 2, false),
        _ => default,
    };

    private readonly GlDevice _device;
    private readonly IBuffer _buffer;
    private uint _textureId;
    private bool _uploaded;
    private DamageRects _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlShmTexture"/> class. It creates no GL object until the first <see cref="Acquire"/>.
    /// </summary>
    /// <param name="device">The device that owns the texture.</param>
    /// <param name="buffer">The buffer to upload from.</param>
    public GlShmTexture(GlDevice device, IBuffer buffer)
    {
        _device = device;
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
        HasAlpha = buffer.Format.HasAlpha();
    }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether the format of the buffer has an alpha channel. Each upload reads it again from the buffer's current format.
    /// </summary>
    public bool HasAlpha { get; private set; }

    /// <summary>
    /// Gets the number of pixel bytes that the texture uploaded since it was created.
    /// </summary>
    public ulong UploadedBytes { get; private set; }

    /// <inheritdoc/>
    public void MarkDirty()
    {
        _uploaded = false;
        _dirty.Clear();
        _dirty.Add(0, 0, Width, Height);
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Uploads the dirty regions when the texture is out of date and gets its name.
    /// </summary>
    /// <param name="textureId">The name of the 2D texture, or 0 on failure. The texture owns it.</param>
    /// <returns><see langword="false"/> when the buffer was not CPU-readable and the texture holds no current content.</returns>
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

            var layout = Layout(view.Format);
            if (layout.BytesPerPixel == 0)
            {
                layout = Layout(DrmFormat.Argb8888);
            }

            gl.BindTexture(TextureTarget.Texture2D, _textureId);
            if (whole)
            {
                var red = layout.SwapRedBlue ? (int)GLEnum.Blue : (int)GLEnum.Red;
                var blue = layout.SwapRedBlue ? (int)GLEnum.Red : (int)GLEnum.Blue;
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, red);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, blue);
            }

            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, view.Stride / layout.BytesPerPixel);
            if (whole)
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)layout.Internal,
                    (uint)Width,
                    (uint)Height,
                    0,
                    (PixelFormat)layout.Format,
                    (PixelType)layout.Type,
                    (void*)view.Data);
                UploadedBytes += (ulong)Width * (ulong)Height * (ulong)layout.BytesPerPixel;
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
                        (PixelFormat)layout.Format,
                        (PixelType)layout.Type,
                        (void*)(view.Data + ((nint)box.Y * view.Stride) + (nint)(box.X * layout.BytesPerPixel)));
                    UploadedBytes += (ulong)box.Width * (ulong)box.Height * (ulong)layout.BytesPerPixel;
                }
            }

            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
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

    /// <summary>
    /// Deletes the GL texture. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_textureId != 0)
        {
            _device.Gl.DeleteTexture(_textureId);
            _textureId = 0;
        }
    }
}
