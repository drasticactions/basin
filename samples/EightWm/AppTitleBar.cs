using Basin;
using Basin.Capabilities;
using Basin.Render.Skia;
using Basin.Scene;
using SkiaSharp;

namespace EightWm;

internal sealed class AppTitleBar : IDisposable
{
    public const int BarHeight = 48;
    public const int CloseWidth = 64;
    public const int RevealBand = 6;
    public const int LeaveSlop = 40;

    private readonly ChromeSurface _surface;
    private readonly SKPaint _fill;
    private readonly SKPaint _stroke;
    private readonly SKFont _title;

    private Box _cell;
    private double _scale = 1;

    public AppTitleBar(IUIHost host, SceneTransform frame)
    {
        Frame = frame;
        _surface = new ChromeSurface(host, frame) { Enabled = false };
        _fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        _stroke = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            Color = SKColors.White,
        });
        _title = SkiaCensus.Track(new SKFont(Fonts.Regular, 15) { Subpixel = true });
    }

    public SceneTransform Frame { get; }

    public Tween Motion;

    public bool Visible { get; private set; }

    public bool HotClose { get; set; }

    public bool Dragging { get; set; }

    public string Title { get; set; } = string.Empty;

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
        var box = CloseBox;
        return x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom;
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
        if (!visible)
        {
            HotClose = false;
            return;
        }

        _surface.Enabled = true;
    }

    public void Retire()
    {
        _surface.Enabled = false;
        HotClose = false;
        Dragging = false;
    }

    public bool Draw()
    {
        if (!Visible)
        {
            return false;
        }

        var box = Box;
        if (box.Width <= 0 || box.Height <= 0)
        {
            return false;
        }

        if (!_surface.Place(box, _scale) || _surface.BeginDraw() is not { } canvas)
        {
            return false;
        }

        try
        {
            canvas.Clear(new SKColor(0xf01f1f1f));

            const float inset = 16f;
            _fill.Color = SKColors.White;
            const float close = CloseWidth;
            canvas.DrawText(
                Fonts.Ellipsize(_title, Title, box.Width - close - (inset * 2)),
                inset, (box.Height / 2f) + (_title.Size / 3), SKTextAlign.Left, _title, _fill);

            if (HotClose)
            {
                _fill.Color = new SKColor(0xffe81123);
                canvas.DrawRect(new SKRect(box.Width - close, 0, box.Width, box.Height), _fill);
            }

            var centerX = box.Width - (close / 2);
            var centerY = box.Height / 2f;
            const float arm = 7f;
            _stroke.StrokeWidth = 2f;
            _stroke.Color = SKColors.White;
            canvas.DrawLine(centerX - arm, centerY - arm, centerX + arm, centerY + arm, _stroke);
            canvas.DrawLine(centerX + arm, centerY - arm, centerX - arm, centerY + arm, _stroke);

            const float grip = 40f;
            _fill.Color = new SKColor(0x44ffffff);
            canvas.DrawRect(
                new SKRect((box.Width - grip) / 2, box.Height - 4, (box.Width + grip) / 2, box.Height - 2),
                _fill);
        }
        finally
        {
            _surface.EndDraw();
        }

        return true;
    }

    public void Dispose()
    {
        SkiaCensus.Release(_title);
        SkiaCensus.Release(_stroke);
        SkiaCensus.Release(_fill);
        _surface.Dispose();
    }
}
