using Basin;
using Basin.Capabilities;
using Basin.Render.Skia;
using Basin.Scene;
using SkiaSharp;

namespace EightWm;

internal sealed class CharmsBar : IDisposable
{
    public const int BarWidth = 88;
    public const int ClockWidth = 320;
    public const int PaneWidth = 364;
    public const int CharmCount = 5;
    public const int CharmSpacing = 96;

    private static readonly string[] Names = ["Search", "Share", "Start", "Devices", "Settings"];

    private static readonly string[] PaneText =
    [
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
    ];

    private readonly ChromeSurface _bar;
    private readonly ChromeSurface _clock;
    private readonly ChromeSurface _pane;
    private readonly SKPaint _fill;
    private readonly SKPaint _stroke;
    private readonly SKFont _label;
    private readonly SKFont _time;
    private readonly SKFont _date;
    private readonly SKFont _title;
    private readonly SKFont _body;

    private int _width;
    private int _height;
    private double _scale = 1;

    public CharmsBar(IUIHost host, SceneTransform barFrame, SceneTransform clockFrame, SceneTransform paneFrame)
    {
        BarFrame = barFrame;
        ClockFrame = clockFrame;
        PaneFrame = paneFrame;
        _bar = new ChromeSurface(host, barFrame) { Enabled = false };
        _clock = new ChromeSurface(host, clockFrame) { Enabled = false };
        _pane = new ChromeSurface(host, paneFrame) { Enabled = false };
        _fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        _stroke = SkiaCensus.Track(new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            Color = SKColors.White,
        });
        _label = SkiaCensus.Track(new SKFont(Fonts.Regular, 12) { Subpixel = true });
        _time = SkiaCensus.Track(new SKFont(Fonts.Regular, 84) { Subpixel = true });
        _date = SkiaCensus.Track(new SKFont(Fonts.Regular, 26) { Subpixel = true });
        _title = SkiaCensus.Track(new SKFont(Fonts.Semibold, 30) { Subpixel = true });
        _body = SkiaCensus.Track(new SKFont(Fonts.Regular, 16) { Subpixel = true });
    }

    public SceneTransform BarFrame { get; }

    public SceneTransform ClockFrame { get; }

    public SceneTransform PaneFrame { get; }

    public Tween BarMotion;

    public Tween ClockMotion;

    public Tween PaneMotion;

    public bool Visible { get; private set; }

    public Charm OpenPane { get; private set; } = Charm.None;

    public Charm Hot { get; set; } = Charm.None;

    public bool ClosingPane { get; set; }

    public string Clock { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public void Resize(int width, int height, double scale)
    {
        _width = width;
        _height = height;
        _scale = scale;
    }

    public Box BarBox
    {
        get
        {
            return new Box(_width - BarWidth, 0, BarWidth, _height);
        }
    }

    public Box PaneBox
    {
        get
        {
            return new Box(_width - PaneWidth, 0, PaneWidth, _height);
        }
    }

    public Charm CharmAt(double x, double y)
    {
        var box = BarBox;
        if (x < box.X || x >= box.Right)
        {
            return Charm.None;
        }

        const double spacing = CharmSpacing;
        var first = (_height / 2.0) - (spacing * ((CharmCount - 1) / 2.0));
        for (var i = 0; i < CharmCount; i++)
        {
            var center = first + (i * spacing);
            if (Math.Abs(y - center) <= spacing / 2)
            {
                return (Charm)i;
            }
        }

        return Charm.None;
    }

    public void Show(bool visible)
    {
        Visible = visible;
        if (!visible)
        {
            Hot = Charm.None;
            return;
        }

        _bar.Enabled = true;
        _clock.Enabled = true;
    }

    public void Retire()
    {
        _bar.Enabled = false;
        _clock.Enabled = false;
        Hot = Charm.None;
    }

    public void RetirePane()
    {
        ClosingPane = false;
        _pane.Enabled = false;
        OpenPane = Charm.None;
    }

    public bool IsRetired => !_bar.Enabled;

    public bool ClockShown => _clock.Enabled;

    public bool PaneShown => _pane.Enabled;

    public bool AnyVisible => Visible || OpenPane != Charm.None;

    public void ShowPane(Charm charm)
    {
        OpenPane = charm;
        _pane.Enabled = charm != Charm.None;
    }

    public bool Draw()
    {
        if (!AnyVisible || _width <= 0 || _height <= 0)
        {
            return false;
        }

        if (Visible)
        {
            DrawBar();
            DrawClock();
        }

        DrawPane();
        return true;
    }

    private void DrawBar()
    {
        var box = BarBox;
        if (!_bar.Place(box, _scale) || _bar.BeginDraw() is not { } canvas)
        {
            return;
        }

        try
        {
            canvas.Clear(new SKColor(0xf01f1f1f));
            const float spacing = CharmSpacing;
            var first = (float)((box.Height / 2.0) - (spacing * ((CharmCount - 1) / 2.0)));
            var centerX = box.Width / 2f;
            for (var i = 0; i < CharmCount; i++)
            {
                var centerY = first + (i * spacing);
                var hot = (int)Hot == i;
                if (hot)
                {
                    _fill.Color = new SKColor(0x33ffffff);
                    canvas.DrawRect(
                        new SKRect(0, centerY - (spacing / 2), box.Width, centerY + (spacing / 2)), _fill);
                }

                DrawGlyph(canvas, (Charm)i, centerX, centerY - 10, 22);
                _fill.Color = SKColors.White;
                canvas.DrawText(
                    Names[i], centerX, centerY + 30, SKTextAlign.Center, _label, _fill);
            }
        }
        finally
        {
            _bar.EndDraw();
        }
    }

    private void DrawClock()
    {
        const int width = ClockWidth;
        const int height = 190;
        const int margin = 40;
        var box = new Box(margin, _height - height - margin, width, height);
        if (!_clock.Place(box, _scale) || _clock.BeginDraw() is not { } canvas)
        {
            return;
        }

        try
        {
            canvas.Clear(SKColors.Transparent);
            _fill.Color = SKColors.White;
            canvas.DrawText(Clock, 0, 90, SKTextAlign.Left, _time, _fill);
            canvas.DrawText(Date, 0, 130, SKTextAlign.Left, _date, _fill);
        }
        finally
        {
            _clock.EndDraw();
        }
    }

    private void DrawPane()
    {
        if (OpenPane == Charm.None)
        {
            return;
        }

        var box = PaneBox;
        if (!_pane.Place(box, _scale) || _pane.BeginDraw() is not { } canvas)
        {
            return;
        }

        try
        {
            canvas.Clear(new SKColor(0xf02b2b2b));
            _fill.Color = SKColors.White;
            const float left = 28f;
            canvas.DrawText(
                Names[(int)OpenPane], left, 80, SKTextAlign.Left, _title, _fill);
            _fill.Color = new SKColor(0xffbbbbbb);
            var text = PaneText[(int)OpenPane];
            var width = box.Width - (left * 2);
            var y = 130f;
            foreach (var line in Wrap(text, width))
            {
                canvas.DrawText(line, left, y, SKTextAlign.Left, _body, _fill);
                y += 24f;
            }
        }
        finally
        {
            _pane.EndDraw();
        }
    }

    private IEnumerable<string> Wrap(string text, float width)
    {
        var start = 0;
        while (start < text.Length)
        {
            var count = (int)_body.BreakText(text.AsSpan(start), width);
            if (count <= 0)
            {
                count = text.Length - start;
            }

            var end = start + count;
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', Math.Min(end, text.Length - 1), count);
                if (space > start)
                {
                    end = space;
                }
            }

            yield return text[start..end].Trim();
            start = end + 1;
        }
    }

    private void DrawGlyph(SKCanvas canvas, Charm charm, float x, float y, float size)
    {
        _stroke.StrokeWidth = Math.Max(2f, size * 0.11f);
        _stroke.Color = SKColors.White;
        _fill.Color = SKColors.White;
        var half = size / 2;
        switch (charm)
        {
            case Charm.Search:
                canvas.DrawCircle(x - (half * 0.2f), y - (half * 0.2f), half * 0.62f, _stroke);
                canvas.DrawLine(
                    x + (half * 0.25f), y + (half * 0.25f), x + (half * 0.85f), y + (half * 0.85f), _stroke);
                break;

            case Charm.Share:
                canvas.DrawLine(x, y + half, x, y - half, _stroke);
                canvas.DrawLine(x, y - half, x - (half * 0.55f), y - (half * 0.35f), _stroke);
                canvas.DrawLine(x, y - half, x + (half * 0.55f), y - (half * 0.35f), _stroke);
                canvas.DrawLine(x - half, y + (half * 0.2f), x - half, y + half, _stroke);
                canvas.DrawLine(x + half, y + (half * 0.2f), x + half, y + half, _stroke);
                canvas.DrawLine(x - half, y + half, x + half, y + half, _stroke);
                break;

            case Charm.Start:
            {
                var gap = size * 0.09f;
                var quadrant = (size - gap) / 2;
                canvas.DrawRect(new SKRect(x - half, y - half, x - half + quadrant, y - half + quadrant), _fill);
                canvas.DrawRect(new SKRect(x + half - quadrant, y - half, x + half, y - half + quadrant), _fill);
                canvas.DrawRect(new SKRect(x - half, y + half - quadrant, x - half + quadrant, y + half), _fill);
                canvas.DrawRect(new SKRect(x + half - quadrant, y + half - quadrant, x + half, y + half), _fill);
                break;
            }

            case Charm.Devices:
                canvas.DrawRect(new SKRect(x - half, y - (half * 0.8f), x + half, y + (half * 0.35f)), _stroke);
                canvas.DrawLine(x - (half * 0.6f), y + half, x + (half * 0.6f), y + half, _stroke);
                break;

            default:
                canvas.DrawCircle(x, y, half * 0.42f, _stroke);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4;
                    var inner = half * 0.62f;
                    var outer = half;
                    canvas.DrawLine(
                        x + (float)(Math.Cos(angle) * inner), y + (float)(Math.Sin(angle) * inner),
                        x + (float)(Math.Cos(angle) * outer), y + (float)(Math.Sin(angle) * outer), _stroke);
                }

                break;
        }
    }

    public void Dispose()
    {
        SkiaCensus.Release(_body);
        SkiaCensus.Release(_title);
        SkiaCensus.Release(_date);
        SkiaCensus.Release(_time);
        SkiaCensus.Release(_label);
        SkiaCensus.Release(_stroke);
        SkiaCensus.Release(_fill);
        _pane.Dispose();
        _clock.Dispose();
        _bar.Dispose();
    }
}
