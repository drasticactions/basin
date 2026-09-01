using Basin.WindowManager;
using SkiaSharp;

namespace DeskbarWm;

internal sealed class TrayView
{
    private readonly List<IApplet> _applets = [];
    private readonly List<Rect> _rects = [];

    public IReadOnlyList<IApplet> Applets => _applets;

    public void SetApplets(IEnumerable<IApplet> applets)
    {
        _applets.Clear();
        _applets.AddRange(applets);
    }

    public int PreferredHeight
    {
        get
        {
            var height = 0;
            foreach (var applet in _applets)
            {
                height = Math.Max(height, applet.PreferredHeight);
            }

            return height > 0 ? height + 4 : 0;
        }
    }

    public string RenderState
    {
        get
        {
            var key = string.Empty;
            foreach (var applet in _applets)
            {
                key += $"|{applet.RenderState}";
            }

            return key;
        }
    }

    public int MeasureWidth(SKFont font, int trayHeight)
    {
        var width = 4;
        foreach (var applet in _applets)
        {
            width += applet.MeasureWidth(font, trayHeight);
        }

        return width;
    }

    public void Layout(Rect trayRect, SKFont font)
    {
        _rects.Clear();
        var x = trayRect.Right - 2;
        foreach (var applet in _applets)
        {
            var width = applet.MeasureWidth(font, trayRect.Height);
            x -= width;
            _rects.Add(new Rect(x, trayRect.Y, width, trayRect.Height));
        }
    }

    public IApplet? AppletAt(Point point)
    {
        for (var i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].Contains(point))
            {
                return _applets[i];
            }
        }

        return null;
    }

    public void Draw(SKCanvas canvas, SKPaint paint, SKFont font)
    {
        for (var i = 0; i < _applets.Count; i++)
        {
            _applets[i].Draw(canvas, paint, font, _rects[i]);
        }
    }
}
