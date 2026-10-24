using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Wraps a buffer as a GL framebuffer. A dmabuf is drawn in place, and a memory buffer is drawn into a texture that <see cref="ReadInto"/> copies back.
/// </summary>
public sealed unsafe class GlRenderTarget
{
    private byte[]? _readbackScratch;

    private float[]? _wideScratch;

    private bool _wide;

    private bool _swap;

    internal static bool IsWide(DrmFormat format) => format is DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f;

    private GlRenderTarget()
    {
    }

    /// <summary>
    /// Gets the name of the framebuffer to draw into.
    /// </summary>
    public uint Framebuffer { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the target is a memory buffer that needs <see cref="ReadInto"/> after a pass.
    /// </summary>
    public bool IsCpuReadback { get; private set; }

    /// <summary>
    /// Gets or sets the write count of the buffer at the time the target last matched its content. A caller rebuilds a CPU target when the count moved.
    /// </summary>
    public ulong SeenWrites { get; set; }

    /// <summary>
    /// Gets the texture that backs a dmabuf target, or 0 for a CPU target.
    /// </summary>
    public uint ColorTexture => IsCpuReadback ? 0u : _textureOrRenderbuffer;

    /// <summary>
    /// Gets the texture that is attached to <see cref="Framebuffer"/>, for both kinds of target.
    /// </summary>
    public uint TargetTexture => _textureOrRenderbuffer;

    /// <summary>
    /// Gets the dmabuf of a dmabuf target, or the default value for a CPU target.
    /// </summary>
    public DmabufAttributes Attributes { get; private set; }

    private uint _textureOrRenderbuffer;
    private EglImage? _image;
    private uint _backdropCopyTexture;
    private uint _backdropCopyFbo;

    /// <summary>
    /// Gets a texture with the pixels drawn so far. A CPU target copies its framebuffer into a separate texture that it keeps.
    /// </summary>
    /// <param name="device">The device whose context is current.</param>
    /// <param name="width">The width of the target in pixels.</param>
    /// <param name="height">The height of the target in pixels.</param>
    /// <returns>The texture name, which the target owns.</returns>
    public uint BackdropTexture(GlDevice device, int width, int height)
    {
        if (ColorTexture != 0)
        {
            return ColorTexture;
        }

        var gl = device.Gl;
        if (_backdropCopyTexture == 0)
        {
            _backdropCopyTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _backdropCopyTexture);
            gl.TexStorage2D(TextureTarget.Texture2D, 1, _wide ? SizedInternalFormat.Rgba16f : SizedInternalFormat.Rgba8, (uint)width, (uint)height);
            _backdropCopyFbo = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _backdropCopyFbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _backdropCopyTexture, 0);
        }

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _backdropCopyFbo);
        gl.BlitFramebuffer(
            0, 0, width, height,
            0, 0, width, height,
            (uint)GLEnum.ColorBufferBit, BlitFramebufferFilter.Nearest);
        return _backdropCopyTexture;
    }

    /// <summary>
    /// Wraps a buffer as a framebuffer and copies the current content of a memory buffer in. It throws <see cref="InvalidOperationException"/> when EGL rejects the dmabuf or the framebuffer is incomplete, and <see cref="NotSupportedException"/> for another memory format.
    /// </summary>
    /// <param name="device">The device whose context is current.</param>
    /// <param name="buffer">A dmabuf, or a memory buffer in a 32-bit RGB format or a 16-bit float format.</param>
    /// <returns>The target, which the caller releases with <see cref="Dispose"/>.</returns>
    public static GlRenderTarget Create(GlDevice device, IBuffer buffer)
    {
        var gl = device.Gl;
        var target = new GlRenderTarget();
        target.Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);

        if (buffer.TryGetDmabuf(out var attributes))
        {
            target.Attributes = attributes;
            target._image = device.ImportDmabufImage(attributes)
                ?? throw new InvalidOperationException("render target dmabuf rejected by EGL");
            target._textureOrRenderbuffer = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, target._textureOrRenderbuffer);
            device.BindImageToTexture2D(target._image);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target._textureOrRenderbuffer, 0);
        }
        else if (IsWide(buffer.Format))
        {
            target.IsCpuReadback = true;
            target._wide = true;
            target._wideScratch = new float[buffer.Width * buffer.Height * 4];
            target._textureOrRenderbuffer = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, target._textureOrRenderbuffer);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            if (buffer.BeginDataAccess(BufferDataAccess.Read, out var seed))
            {
                try
                {
                    var rows = new byte[buffer.Width * buffer.Height * 8];
                    for (var y = 0; y < buffer.Height; y++)
                    {
                        new ReadOnlySpan<byte>((byte*)(seed.Data + y * seed.Stride), buffer.Width * 8)
                            .CopyTo(rows.AsSpan(y * buffer.Width * 8));
                    }

                    fixed (byte* rowsPtr = rows)
                    {
                        gl.TexImage2D(
                            TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                            (uint)buffer.Width, (uint)buffer.Height, 0,
                            PixelFormat.Rgba, PixelType.HalfFloat, rowsPtr);
                    }
                }
                finally
                {
                    buffer.EndDataAccess();
                }
            }
            else
            {
                gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba16f, (uint)buffer.Width, (uint)buffer.Height);
            }

            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target._textureOrRenderbuffer, 0);
        }
        else
        {
            if (buffer.Format is not (DrmFormat.Argb8888 or DrmFormat.Xrgb8888 or DrmFormat.Abgr8888 or DrmFormat.Xbgr8888))
            {
                gl.DeleteFramebuffer(target.Framebuffer);
                throw new NotSupportedException($"{buffer.Format} is not a CPU render target format for the GL renderer.");
            }

            target.IsCpuReadback = true;
            target._swap = buffer.Format is DrmFormat.Argb8888 or DrmFormat.Xrgb8888;
            var swap = target._swap;
            target._readbackScratch = new byte[buffer.Width * buffer.Height * 4];
            target._textureOrRenderbuffer = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, target._textureOrRenderbuffer);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            var scratch = target._readbackScratch;
            if (buffer.BeginDataAccess(BufferDataAccess.Read, out var seed))
            {
                try
                {
                    for (var y = 0; y < buffer.Height; y++)
                    {
                        var row = (byte*)(seed.Data + y * seed.Stride);
                        var destination = y * buffer.Width * 4;
                        for (var x = 0; x < buffer.Width; x++)
                        {
                            scratch[destination + x * 4 + 0] = row[x * 4 + (swap ? 2 : 0)];
                            scratch[destination + x * 4 + 1] = row[x * 4 + 1];
                            scratch[destination + x * 4 + 2] = row[x * 4 + (swap ? 0 : 2)];
                            scratch[destination + x * 4 + 3] = row[x * 4 + 3];
                        }
                    }
                }
                finally
                {
                    buffer.EndDataAccess();
                }
            }

            fixed (byte* seedPtr = scratch)
            {
                gl.TexImage2D(
                    TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                    (uint)buffer.Width, (uint)buffer.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, seedPtr);
            }

            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target._textureOrRenderbuffer, 0);
        }

        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("render target framebuffer incomplete");
        }

        return target;
    }

    /// <summary>
    /// Reads the framebuffer of a CPU target back into a memory buffer. It blocks until the GPU finished drawing.
    /// </summary>
    /// <param name="device">The device whose context is current.</param>
    /// <param name="target">The buffer that the target wraps. Nothing is written when it is not CPU-writable.</param>
    public void ReadInto(GlDevice device, IBuffer target)
    {
        if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return;
        }

        try
        {
            var gl = device.Gl;
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            if (_wide)
            {
                var wide = _wideScratch!;
                fixed (float* widePtr = wide)
                {
                    gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.Float, widePtr);
                }

                for (var y = 0; y < target.Height; y++)
                {
                    var row = (Half*)(view.Data + y * view.Stride);
                    var src = y * target.Width * 4;
                    for (var i = 0; i < target.Width * 4; i++)
                    {
                        row[i] = (Half)wide[src + i];
                    }
                }

                return;
            }

            var scratch = _readbackScratch!;
            fixed (byte* scratchPtr = scratch)
            {
                gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.UnsignedByte, scratchPtr);
            }

            for (var y = 0; y < target.Height; y++)
            {
                var row = (byte*)(view.Data + y * view.Stride);
                var src = y * target.Width * 4;
                for (var x = 0; x < target.Width; x++)
                {
                    row[x * 4 + 0] = scratch[src + x * 4 + (_swap ? 2 : 0)];
                    row[x * 4 + 1] = scratch[src + x * 4 + 1];
                    row[x * 4 + 2] = scratch[src + x * 4 + (_swap ? 0 : 2)];
                    row[x * 4 + 3] = scratch[src + x * 4 + 3];
                }
            }
        }
        finally
        {
            target.EndDataAccess();
        }
    }

    /// <summary>
    /// Deletes the framebuffer, the textures and the EGL image, and raises <see cref="GlDevice.TextureDeleting"/> for each texture that an effect can hold.
    /// </summary>
    /// <param name="device">The device that created the target.</param>
    public void Dispose(GlDevice device)
    {
        var gl = device.Gl;
        if (_backdropCopyTexture != 0)
        {
            device.NotifyTextureDeleting(_backdropCopyTexture);
            gl.DeleteFramebuffer(_backdropCopyFbo);
            gl.DeleteTexture(_backdropCopyTexture);
        }

        if (ColorTexture != 0)
        {
            device.NotifyTextureDeleting(ColorTexture);
        }

        gl.DeleteFramebuffer(Framebuffer);
        gl.DeleteTexture(_textureOrRenderbuffer);
        _image?.Dispose();
    }
}
