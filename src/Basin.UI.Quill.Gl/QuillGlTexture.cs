namespace Basin.UI.Quill;

public sealed class QuillGlTexture : QuillTexture
{
    internal QuillGlTexture(uint name, int width, int height)
        : base(width, height)
    {
        Name = name;
    }

    public uint Name { get; }
}
