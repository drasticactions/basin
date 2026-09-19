using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

public interface IWindowContent
{
    string Title { get; }

    string AppId { get; }

    Box Geometry { get; }

    int MinWidth { get; }

    int MinHeight { get; }

    int MaxWidth { get; }

    int MaxHeight { get; }

    IWindowContent? Parent { get; }

    Surface? Surface { get; }

    IUISurface? UISurface { get; }

    bool Maximized { get; }

    bool Fullscreen { get; }

    bool Resizing { get; }

    bool ServerDecorated { get; }

    bool Centered { get; }

    FrameCapabilities Capabilities { get; }

    event Action? TitleChanged;

    event Action? AppIdChanged;

    event Action? ParentChanged;

    event Action? DecorationsChanged;

    event Action? Committed;

    SceneNode Attach(SceneTree tree);

    void Detach();

    void SetActivated(bool activated);

    void SetMaximized(bool maximized);

    void SetFullscreen(bool fullscreen);

    void SetResizing(bool resizing);

    void SetMinimized(bool minimized);

    void Raise();

    void Lower();

    void SetTiled(ResizeEdges edges);

    void SetSize(int width, int height);

    void SetBounds(int width, int height);

    void SetPosition(int x, int y);

    void OutputScaleChanged(double scale);

    void ReportGeometry(in Box frame, in Box client);

    void Close();

    bool Owns(Surface surface);
}
