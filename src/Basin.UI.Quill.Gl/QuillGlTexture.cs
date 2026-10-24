namespace Basin.UI.Quill;

/// <summary>
/// A GL texture of a Quill canvas, made by <see cref="QuillTextures"/>.
/// </summary>
public sealed class QuillGlTexture : QuillTexture
{
    internal QuillGlTexture(uint name, int width, int height)
        : base(width, height)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the GL texture name. It is deleted when the pool destroys the texture.
    /// </summary>
    public uint Name { get; }
}
