using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Westonia.Shell;

namespace Westonia;

internal sealed class ShellFrame : IDisposable
{
    public const int Margin = FrameModel.Margin;

    public const int BorderWidth = FrameModel.BorderWidth;

    public const int TitlebarHeight = FrameModel.TitlebarHeight;

    public const int InsetX = Margin + BorderWidth;

    public const int InsetY = Margin + TitlebarHeight;

    private readonly ShellWindow _window;
    private readonly FrameModel _title = new();
    private readonly FrameEdgeModel[] _edges =
    [
        new(FrameEdge.Left),
        new(FrameEdge.Right),
        new(FrameEdge.Bottom),
    ];

    private readonly UIStripFrame _frame;

    public ShellFrame(AvaloniaUIHost host, SceneTree parent, ShellWindow window, UISurfaceIndex index)
    {
        _window = window;
        _title.Title = window.Window.Title;
        _frame = new UIStripFrame(host, parent, new UIStripFrameMetrics(Margin, BorderWidth, TitlebarHeight), Fill, index) { AccessibleTitle = _title.Title };
    }

    public SceneTree Parent => _frame.Parent;

    public Box OuterBox => _frame.OuterBox;

    public bool OwnsSurface(IUISurface surface) => _frame.OwnsSurface(surface);

    public void SetActive(bool active)
    {
        _title.Active = active;
        foreach (var edge in _edges)
        {
            edge.Active = active;
        }
    }

    public void SetTitle(string title)
    {
        _title.Title = title;
        _frame.AccessibleTitle = title;
    }

    public bool Update(double scale)
    {
        var geometry = _window.Window.Xdg.PresentedGeometry;
        return _frame.Update(_window.X, _window.Y, geometry.Width, geometry.Height, scale);
    }

    public bool AcceptsInputAt(double x, double y) => _frame.AcceptsInputAt(x, y);

    public bool HitsTitlebar(double x, double y) => _frame.HitsTitlebar(x, y);

    public bool HitsClose(double x, double y)
    {
        var box = OuterBox;
        var localX = x - box.X;
        var localY = y - box.Y;
        var right = box.Width - Margin - BorderWidth;
        return localY >= Margin + 4 && localY < Margin + 23 &&
               localX >= right - 19 && localX < right;
    }

    public ResizeEdges EdgeAt(double x, double y) => (ResizeEdges)_frame.EdgeAt(x, y);

    public void Dispose() => _frame.Dispose();

    private void Fill(UIFrameStrip strip, IUISurface surface)
    {
        if (surface is not AvaloniaUISurface avalonia)
        {
            return;
        }

        avalonia.Content = strip switch
        {
            UIFrameStrip.Title => new FrameTitleView { DataContext = _title },
            UIFrameStrip.Left => new FrameEdgeView { DataContext = _edges[0] },
            UIFrameStrip.Right => new FrameEdgeView { DataContext = _edges[1] },
            _ => new FrameEdgeView { DataContext = _edges[2] },
        };
    }
}
