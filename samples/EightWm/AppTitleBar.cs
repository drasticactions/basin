using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace EightWm;

internal sealed class AppTitleBar : IDisposable
{
    public const int BarHeight = 48;
    public const int CloseWidth = 64;
    public const int RevealBand = 6;
    public const int LeaveSlop = 40;

    private readonly AvaloniaChrome _chrome;
    private readonly TitleBarView _view;

    private Box _cell;
    private double _scale = 1;

    public AppTitleBar(IUIHost host, UISurfaceIndex index, SceneTransform frame, Action close)
    {
        Frame = frame;
        Model = new TitleModel(close);
        _view = new TitleBarView { DataContext = Model };
        _chrome = new AvaloniaChrome(frame, host, index, _view) { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public TitleModel Model { get; }

    public AvaloniaChrome Chrome => _chrome;

    public Tween Motion;

    public bool Visible { get; private set; }

    public bool Dragging { get; set; }

    public AppWindow? App { get; set; }

    public string Title
    {
        get => Model.Title;
        set => Model.Title = value;
    }

    public double Scale => _scale;

    public Box Box => new(_cell.X, _cell.Y, _cell.Width, Thickness);

    public int Thickness => BarHeight;

    public Box CloseBox
    {
        get
        {
            const int width = CloseWidth;
            var box = Box;
            return new Box(box.Right - width, box.Y, width, box.Height);
        }
    }

    public bool Holds(double x, double y)
    {
        var box = Box;
        return x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom;
    }

    public bool HoldsClose(double x, double y)
    {
        if (!Holds(x, y))
        {
            return false;
        }

        var box = Box;
        return _chrome.Surface is null
            ? x >= CloseBox.X
            : _view.IsOverClose(x - box.X, y - box.Y);
    }

    public bool NearTop(double x, double y)
    {
        var box = Box;
        return x >= box.X && x < box.Right && y >= box.Y - 1 && y <= box.Y + RevealBand;
    }

    public bool HasLeft(double y) => y > Box.Bottom + LeaveSlop;

    public void Resize(in Box cell, double scale)
    {
        _cell = cell;
        _scale = scale;
    }

    public void Show(bool visible)
    {
        Visible = visible;
        if (visible)
        {
            _chrome.Enabled = true;
        }
    }

    public void Retire()
    {
        _chrome.Enabled = false;
        Dragging = false;
        App = null;
    }

    public bool Draw()
    {
        var box = Box;
        return Visible && box.Width > 0 && box.Height > 0 && _chrome.Place(box, _scale);
    }

    public void Dispose() => _chrome.Dispose();
}
