using Basin.Diagnostics;

namespace Basin.UI.Quill;

public abstract class QuillTexture
{
    private protected QuillTexture(int width, int height)
    {
        Width = width;
        Height = height;
        BasinCounters.Track();
    }

    public int Width { get; }

    public int Height { get; }

    internal void Retire() => BasinCounters.Untrack();
}
