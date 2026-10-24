using Avalonia.Controls;
using Basin;
using Basin.Scene;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed class TabOverlay(TabSet set, AvaloniaUISurface surface, UISurfaceNode node, SceneRect dim, Box box, Control view, LauncherViewModel? model) : IDisposable
{
    public TabSet Set { get; } = set;

    public AvaloniaUISurface Surface { get; } = surface;

    public Box Box { get; } = box;

    public Control View { get; } = view;

    public LauncherViewModel? Model { get; } = model;

    public bool Contains(double x, double y) => x >= Box.X && y >= Box.Y && x < Box.X + Box.Width && y < Box.Y + Box.Height;

    public void Dispose()
    {
        node.Dispose();
        Surface.Dispose();
        dim.Destroy();
    }
}
