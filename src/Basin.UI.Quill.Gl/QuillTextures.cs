using Pixman;
using Silk.NET.OpenGLES;

namespace Basin.UI.Quill;

public sealed unsafe class QuillTextures : IDisposable
{
    private readonly GL _gl;
    private readonly QuillGlContext? _context;
    private readonly List<QuillGlTexture> _textures = [];
    private bool _disposed;

    internal QuillTextures(GL gl, QuillGlContext? context = null)
    {
        _gl = gl;
        _context = context;
    }

    public int Count => _textures.Count;

    public QuillGlTexture Create(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        using var current = Enter();
        var name = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, name);
        _gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        var texture = new QuillGlTexture(name, width, height);
        _textures.Add(texture);
        return texture;
    }

    public void Update(QuillGlTexture texture, in Box bounds, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_disposed || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var needed = bounds.Width * bounds.Height * 4;
        if (rgba.Length < needed)
        {
            return;
        }

        using var current = Enter();
        _gl.BindTexture(TextureTarget.Texture2D, texture.Name);
        fixed (byte* pixels = rgba)
        {
            _gl.TexSubImage2D(
                TextureTarget.Texture2D, 0, bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Destroy(QuillGlTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!_textures.Remove(texture))
        {
            return;
        }

        using var current = Enter();
        _gl.DeleteTexture(texture.Name);
        texture.Retire();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using var current = Enter();
        for (var i = 0; i < _textures.Count; i++)
        {
            _gl.DeleteTexture(_textures[i].Name);
            _textures[i].Retire();
        }

        _textures.Clear();
    }

    private QuillGlContext.Scope Enter() => _context?.Enter() ?? default;
}
