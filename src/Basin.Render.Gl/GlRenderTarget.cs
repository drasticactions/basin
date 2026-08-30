using Mesa.Egl;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

public sealed unsafe class GlRenderTarget
{
    private byte[]? _readbackScratch;

    private GlRenderTarget()
    {
    }

    public uint Framebuffer { get; private set; }

    public bool IsCpuReadback { get; private set; }

    public uint ColorTexture => IsCpuReadback ? 0u : _textureOrRenderbuffer;

    public uint TargetTexture => _textureOrRenderbuffer;

    public DmabufAttributes Attributes { get; private set; }

    private uint _textureOrRenderbuffer;
    private EglImage? _image;

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
        else
        {
            target.IsCpuReadback = true;
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
                            scratch[destination + x * 4 + 0] = row[x * 4 + 2];
                            scratch[destination + x * 4 + 1] = row[x * 4 + 1];
                            scratch[destination + x * 4 + 2] = row[x * 4 + 0];
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
                    row[x * 4 + 0] = scratch[src + x * 4 + 2];
                    row[x * 4 + 1] = scratch[src + x * 4 + 1];
                    row[x * 4 + 2] = scratch[src + x * 4 + 0];
                    row[x * 4 + 3] = scratch[src + x * 4 + 3];
                }
            }
        }
        finally
        {
            target.EndDataAccess();
        }
    }

    public void Dispose(GlDevice device)
    {
        var gl = device.Gl;
        gl.DeleteFramebuffer(Framebuffer);
        gl.DeleteTexture(_textureOrRenderbuffer);
        _image?.Dispose();
    }
}
