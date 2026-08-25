using Basin;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaThumbnail : IDisposable
{
    private bool _disposed;

    public PlasmaThumbnail(SceneTree parent, PlasmaHostView view, int contentWidth, int contentHeight)
    {
        View = view;
        Slot = new SceneTree(parent);
        Frame = new SceneTransform(Slot);
        Mirror = new SceneMirror(Frame, view.Tree, contentWidth, contentHeight);
    }

    public PlasmaHostView View { get; }

    public SceneTree Slot { get; }

    public SceneTransform Frame { get; }

    public SceneMirror Mirror { get; }

    public Box Box { get; private set; }

    public void Place(in Box box, in Box content, int contentWidth, int contentHeight, int sourceX, int sourceY)
    {
        Box = box;
        Slot.SetPosition(content.X, content.Y);
        Slot.ClipBox = new Box(0, 0, content.Width, content.Height);
        Mirror.Width = contentWidth;
        Mirror.Height = contentHeight;
        Mirror.SourceX = -sourceX;
        Mirror.SourceY = -sourceY;

        var factor = Math.Min(
            1.0, Math.Min(content.Width / (double)contentWidth, content.Height / (double)contentHeight));
        Frame.SetPosition(
            (int)Math.Round((content.Width - (contentWidth * factor)) / 2),
            (int)Math.Round((content.Height - (contentHeight * factor)) / 2));
        Frame.Matrix = RenderTransform.Scale(factor, factor);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Mirror.Destroy();
        Frame.Destroy();
        Slot.Destroy();
    }
}
