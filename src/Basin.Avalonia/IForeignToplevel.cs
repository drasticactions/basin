using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public interface IForeignToplevel
{
    Surface Surface { get; }

    string Title { get; }

    string AppId { get; }

    int Width { get; }

    int Height { get; }

    bool ServerDecorated { get; }

    bool IsPopup { get; }

    Surface? AnchorSurface { get; }

    int AnchorOffsetX { get; }

    int AnchorOffsetY { get; }

    event Action? TitleChanged;

    event Action? GeometryChanged;

    event Action? Closed;

    void Resize(int width, int height);

    void Close();

    void Activate(bool active);
}
