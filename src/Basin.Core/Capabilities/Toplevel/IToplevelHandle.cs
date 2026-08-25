namespace Basin.Capabilities;

public interface IToplevelHandle
{
    Surface? Surface { get; }

    string Title { get; }

    string AppId { get; }

    IToplevelHandle? Parent { get; }

    bool WantsFocus { get; }

    (int Width, int Height) NaturalSize { get; }

    void SetActivated(bool activated);

    void SetMaximized(bool maximized);

    void SetFullscreen(bool fullscreen);

    void Configure(int x, int y, int width, int height);

    void Close();

    bool IsTransientFor(IToplevelHandle other)
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, other))
            {
                return true;
            }
        }

        return false;
    }
}
