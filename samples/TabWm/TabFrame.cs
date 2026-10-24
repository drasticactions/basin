using Avalonia;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed class TabFrame : IDisposable
{
    public const int Border = 4;

    public const int Titlebar = 30;

    public const int CaptionButtons = (3 * 30) + 4;

    private readonly UIStripFrame _frame;

    public TabFrame(IUIHost host, SceneTree parent, UISurfaceIndex index, FrameModel model)
    {
        Model = model;
        _frame = new UIStripFrame(host, parent, new UIStripFrameMetrics(0, Border, Titlebar), Fill, index);
    }

    public FrameModel Model { get; }

    public UIStripFrame Strips => _frame;

    public Box OuterBox => _frame.OuterBox;

    public static FrameInsets Insets => new(Titlebar, Border, Border, Border);

    public bool OwnsSurface(IUISurface surface) => _frame.OwnsSurface(surface);

    public void Update(int x, int y, int width, int height, double scale) => _frame.Update(x, y, width, height, scale);

    public void SetActive(bool active) => Model.Active = active;

    public void SetEnabled(bool enabled) => _frame.SetEnabled(enabled);

    public bool AcceptsInputAt(double x, double y) => _frame.AcceptsInputAt(x, y);

    public bool HitsTitlebar(double x, double y) => _frame.HitsTitlebar(x, y) && !HitsButtons(x, y);

    public bool HitsButtons(double x, double y)
    {
        var box = OuterBox;
        return _frame.HitsTitlebar(x, y) && x >= box.X + box.Width - Border - CaptionButtons;
    }

    public FrameEdges EdgeAt(double x, double y) => _frame.EdgeAt(x, y);

    public void Dispose() => _frame.Dispose();

    private void Fill(UIFrameStrip strip, IUISurface surface)
    {
        if (surface is not AvaloniaUISurface avalonia)
        {
            return;
        }

        avalonia.Content = strip switch
        {
            UIFrameStrip.Title => new FrameTitleView { DataContext = Model },
            UIFrameStrip.Left => new FrameEdgeView(new Thickness(1, 0, 0, 0)) { DataContext = Model },
            UIFrameStrip.Right => new FrameEdgeView(new Thickness(0, 0, 1, 0)) { DataContext = Model },
            _ => new FrameEdgeView(new Thickness(1, 0, 1, 1)) { DataContext = Model },
        };
    }
}
