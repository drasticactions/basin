using Basin.Diagnostics;

namespace Basin.UI.Quill;

/// <summary>
/// A texture that a Quill renderer made for a canvas. Each backend has its own subclass. The texture is counted in <see cref="BasinCounters"/> until it is destroyed.
/// </summary>
public abstract class QuillTexture
{
    private protected QuillTexture(int width, int height)
    {
        Width = width;
        Height = height;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the width in texels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in texels.
    /// </summary>
    public int Height { get; }

    internal void Retire() => BasinCounters.Untrack();
}
