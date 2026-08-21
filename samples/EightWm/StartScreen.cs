using Basin;
using Basin.Capabilities;
using Basin.Render.Skia;
using Basin.Scene;
using SkiaSharp;

namespace EightWm;

internal sealed class StartScreen : IDisposable
{
    public const int SidePadding = 60;
    public const int TopPadding = 130;
    private const int LabelHeight = 26;

    private readonly ChromeSurface _chrome;
    private readonly IconLoader _icons;
    private readonly TileGrid _grid = new();
    private readonly SKPaint _fill;
    private readonly SKFont _label;
    private readonly SKFont _header;
    private readonly SKFont _badge;

    private int _width;
    private int _height;
    private double _scale = 1;
    private bool _laid;
    private bool _dirty = true;
    private double _drawnPan;
    private double _drawnAppsPan;
    private double _drawnZoom;
    private double _drawnLabels;
    private double _drawnCenterX;
    private double _drawnCenterY;
    private double _drawnDragX;
    private double _drawnDragY;
    private Tile? _drawnPressed;
    private Tile? _drawnSlide;
    private CrossSlideStage _drawnStage;
    private bool _drawnApps;
    private uint _drawnBackground;

    private readonly SceneTransform _pressFrame;
    private readonly ChromeSurface _press;
    private readonly TileTilt _tilt = new();

    public StartScreen(IUIHost host, SceneTree parent, IconLoader icons, uint background)
    {
        _chrome = new ChromeSurface(host, parent);
        _pressFrame = new SceneTransform(parent);
        _press = new ChromeSurface(host, _pressFrame) { Enabled = false };
        _icons = icons;
        Background = background;
        _fill = SkiaCensus.Track(new SKPaint { IsAntialias = true });
        _label = SkiaCensus.Track(new SKFont(Fonts.Regular, 15) { Subpixel = true });
        _header = SkiaCensus.Track(new SKFont(Fonts.Semibold, 34) { Subpixel = true });
        _badge = SkiaCensus.Track(new SKFont(Fonts.Semibold, 22) { Subpixel = true });
        Pan = new Manipulation
        {
            RailSlop = 12,
            Rail = PanAxis.Horizontal,
            Snap = SnapKind.Proximity,
            ProximityRange = 140,
        };
    }

    public TileGrid Grid => _grid;

    private uint _background;

    public uint Background
    {
        get => _background;
        set => _background = value;
    }

    public void Invalidate() => _dirty = true;

    public bool Dirty
    {
        get
        {
            if (_dirty || !_laid || _drawnApps != AppsVisible || _drawnBackground != Background)
            {
                return true;
            }

            if (_drawnPan != Pan.Offset || _drawnAppsPan != AppsPan.Offset ||
                _drawnZoom != Zoom || _drawnLabels != LabelAlpha ||
                _drawnCenterX != ZoomCenterX || _drawnCenterY != ZoomCenterY)
            {
                return true;
            }

            if (!ReferenceEquals(_drawnPressed, _pressed) ||
                !ReferenceEquals(_drawnSlide, Slide.Tile) || _drawnStage != Slide.Stage)
            {
                return true;
            }

            if (Slide.Tile is { } dragged && (_drawnDragX != dragged.DragX || _drawnDragY != dragged.DragY))
            {
                return true;
            }

            return AnyTileMoving();
        }
    }

    private bool AnyTileMoving()
    {
        foreach (var group in (AppsVisible ? Apps : _grid).Groups)
        {
            foreach (var tile in group.Tiles)
            {
                if (tile.Press.IsRunning || tile.Check.IsRunning)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RecordDrawn()
    {
        _dirty = false;
        _drawnApps = AppsVisible;
        _drawnBackground = Background;
        _drawnPan = Pan.Offset;
        _drawnAppsPan = AppsPan.Offset;
        _drawnZoom = Zoom;
        _drawnLabels = LabelAlpha;
        _drawnCenterX = ZoomCenterX;
        _drawnCenterY = ZoomCenterY;
        _drawnPressed = _pressed;
        _drawnSlide = Slide.Tile;
        _drawnStage = Slide.Stage;
        _drawnDragX = Slide.Tile?.DragX ?? 0;
        _drawnDragY = Slide.Tile?.DragY ?? 0;
    }

    public Manipulation Pan;

    public bool Enabled
    {
        get => _chrome.Enabled;
        set => _chrome.Enabled = value;
    }

    public bool ZoomedOut { get; private set; }

    private Tile? _pressed;

    public Tile? Pressed
    {
        get => _pressed;
        set
        {
            if (ReferenceEquals(_pressed, value))
            {
                return;
            }

            _pressed = value;
            _dirty = true;
            if (value is null)
            {
                _press.Enabled = false;
                _pressFrame.Deformer = null;
            }
        }
    }

    public bool Tilt { get; set; } = true;

    public void SetContact(double x, double y)
    {
        if (_pressed is not { } tile)
        {
            return;
        }

        var side = SidePadding;
        var top = TopPadding;
        _tilt.SetContact(
            x - side - Pan.Offset - tile.Box.X, y - top - tile.Box.Y,
            new Box(0, 0, tile.Box.Width, tile.Box.Height));
    }

    public Tween ZoomMotion;

    public double ZoomCenterX { get; private set; }

    public double ZoomCenterY { get; private set; }

    public const double OverviewScale = 0.45;

    public bool AppsVisible { get; set; }

    public TileGrid Apps { get; } = new();

    public Manipulation AppsPan = new() { RailSlop = 12, Rail = PanAxis.Horizontal };

    public CrossSlide Slide = new();

    public double Zoom
    {
        get
        {
            var progress = ZoomMotion.IsRunning ? Math.Clamp(ZoomMotion.Alpha, 0, 1) : 1;
            return ZoomedOut
                ? 1 + ((OverviewScale - 1) * progress)
                : OverviewScale + ((1 - OverviewScale) * progress);
        }
    }

    public double LabelAlpha
    {
        get
        {
            var progress = ZoomMotion.IsRunning ? Math.Clamp(ZoomMotion.Alpha, 0, 1) : 1;
            return ZoomedOut ? progress : 1 - progress;
        }
    }

    public void SetZoom(bool zoomedOut, double centerX, double centerY, in AnimationSpec spec, long nowMillis)
    {
        if (ZoomedOut == zoomedOut)
        {
            return;
        }

        ZoomedOut = zoomedOut;
        ZoomCenterX = centerX;
        ZoomCenterY = centerY;
        ZoomMotion.Start(spec, nowMillis);
    }

    public void SetZoomNow(bool zoomedOut)
    {
        ZoomedOut = zoomedOut;
        ZoomMotion.Stop();
    }

    public void SetApps(IEnumerable<DesktopEntry> entries)
    {
        Apps.Clear();
        foreach (var entry in entries)
        {
            Apps.Add(new Tile
            {
                Name = entry.Name,
                Exec = entry.Exec,
                Icon = entry.Icon ?? Path.GetFileNameWithoutExtension(entry.Id),
                Size = TileSize.Small,
                Color = 0x33ffffff,
                Group = "Apps",
            });
        }

        _laid = false;
    }

    public void SetTiles(IEnumerable<Tile> tiles, IReadOnlyList<string> groupOrder)
    {
        _grid.Clear();
        foreach (var tile in tiles)
        {
            _grid.Add(tile);
        }

        if (groupOrder.Count > 0)
        {
            _grid.Groups.Sort((left, right) =>
            {
                var a = Rank(groupOrder, left.Name);
                var b = Rank(groupOrder, right.Name);
                return a != b ? a.CompareTo(b) : string.CompareOrdinal(left.Name, right.Name);
            });
        }

        _laid = false;
    }

    private static int Rank(IReadOnlyList<string> order, string name)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == name)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    public void Resize(int width, int height, double scale)
    {
        if (_width == width && _height == height && _scale == scale)
        {
            return;
        }

        _width = width;
        _height = height;
        _scale = scale;
        _laid = false;
    }

    public void Layout()
    {
        if (_laid || _width <= 0 || _height <= 0)
        {
            return;
        }

        const int top = TopPadding;
        const int bottom = 60;
        var available = Math.Max(1, _height - top - bottom);
        _grid.Layout(available);
        Apps.Layout(available);
        const int side = SidePadding;
        Pan.Minimum = Math.Min(0, _width - side - side - _grid.Width);
        Pan.Maximum = 0;
        AppsPan.Minimum = Math.Min(0, _width - side - side - Apps.Width);
        AppsPan.Maximum = 0;
        _laid = true;
    }

    public Tile? TileAt(double x, double y)
    {
        Layout();
        var side = SidePadding;
        var top = TopPadding;
        if (AppsVisible)
        {
            return Apps.At(x - side - AppsPan.Offset, y - top);
        }

        var zoom = Zoom;
        var localX = ((x - side - Pan.Offset - ZoomCenterX) / zoom) + ZoomCenterX;
        var localY = ((y - top - ZoomCenterY) / zoom) + ZoomCenterY;
        return _grid.At(localX, localY);
    }

    public TileGroup? GroupAt(double x, double y)
    {
        Layout();
        var side = SidePadding;
        var top = TopPadding;
        var zoom = Zoom;
        var localX = ((x - side - Pan.Offset - ZoomCenterX) / zoom) + ZoomCenterX;
        var localY = ((y - top - ZoomCenterY) / zoom) + ZoomCenterY;
        foreach (var group in _grid.Groups)
        {
            var box = group.Box;
            if (localX >= box.X && localX < box.Right && localY >= box.Y - 20 && localY < box.Bottom + 60)
            {
                return group;
            }
        }

        return null;
    }

    public bool Draw()
    {
        Layout();
        if (!_chrome.Place(new Box(0, 0, _width, _height), _scale))
        {
            return false;
        }

        if (_chrome.BeginDraw() is not { } canvas)
        {
            return false;
        }

        try
        {
            canvas.Clear(new SKColor(Background));
            var side = SidePadding;
            var top = TopPadding;

            _fill.Color = SKColors.White;
            canvas.DrawText(
                AppsVisible ? "Apps" : "Start", side, 70f, SKTextAlign.Left, _header,
                _fill);

            if (AppsVisible)
            {
                canvas.Save();
                canvas.Translate(side + (float)AppsPan.Offset, top);
                foreach (var group in Apps.Groups)
                {
                    foreach (var tile in group.Tiles)
                    {
                        DrawTile(canvas, tile);
                    }
                }

                canvas.Restore();
                return true;
            }

            canvas.Save();
            canvas.Translate(side + (float)Pan.Offset, top);
            var zoom = (float)Zoom;
            if (zoom != 1f)
            {
                canvas.Translate((float)ZoomCenterX, (float)ZoomCenterY);
                canvas.Scale(zoom);
                canvas.Translate(-(float)ZoomCenterX, -(float)ZoomCenterY);
            }

            foreach (var group in _grid.Groups)
            {
                foreach (var tile in group.Tiles)
                {
                    if (ReferenceEquals(tile, _pressed) && Tilt)
                    {
                        continue;
                    }

                    if (!ReferenceEquals(tile, Slide.Tile) || Slide.Stage != CrossSlideStage.Detached)
                    {
                        DrawTile(canvas, tile);
                    }
                }

                var labels = (float)LabelAlpha;
                if (labels > 0.01f)
                {
                    _fill.Color = new SKColor(0xffffffff).WithAlpha((byte)(labels * 160));
                    canvas.DrawText(
                        group.Name, group.Box.X, group.Box.Bottom + (34 / zoom), SKTextAlign.Left, _header, _fill);
                }
            }

            if (Slide is { Stage: CrossSlideStage.Detached, Tile: { } dragged })
            {
                canvas.Save();
                canvas.Translate((float)dragged.DragX, (float)dragged.DragY);
                DrawTile(canvas, dragged, ghost: true);
                canvas.Restore();
            }

            canvas.Restore();
        }
        finally
        {
            _chrome.EndDraw();
            RecordDrawn();
        }

        DrawPressed();
        return true;
    }

    private void DrawPressed()
    {
        if (!Tilt || _pressed is not { } tile || AppsVisible)
        {
            _press.Enabled = false;
            _pressFrame.Deformer = null;
            return;
        }

        var side = SidePadding;
        var top = TopPadding;
        _press.Enabled = true;
        _pressFrame.SetPosition(side + (int)Math.Round(Pan.Offset) + tile.Box.X, top + tile.Box.Y);
        if (!_press.Place(new Box(0, 0, tile.Box.Width, tile.Box.Height), _scale))
        {
            return;
        }

        if (_press.BeginDraw() is { } canvas)
        {
            try
            {
                canvas.Save();
                canvas.Translate(-tile.Box.X, -tile.Box.Y);
                DrawTile(canvas, tile, ghost: false, plain: true);
                canvas.Restore();
            }
            finally
            {
                _press.EndDraw();
            }
        }

        _tilt.Press = tile.Press.IsRunning
            ? Math.Clamp((1 - tile.Press.Scale) / 0.025, 0, 1)
            : 1;
        _pressFrame.Deformer = _tilt;
        _pressFrame.NotifyDeformed();
    }

    private void DrawTile(SKCanvas canvas, Tile tile, bool ghost = false, bool plain = false)
    {
        var box = tile.Box;
        var rect = new SKRect(box.X, box.Y, box.Right, box.Bottom);
        var pressed = !plain && (tile.Press.IsRunning || ReferenceEquals(tile, _pressed));

        canvas.Save();
        if (pressed)
        {
            var press = (float)(tile.Press.Scale <= 0 ? 0.975 : tile.Press.Scale);
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(press);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        _fill.Color = ghost ? new SKColor(tile.Color).WithAlpha(180) : new SKColor(tile.Color);
        canvas.DrawRect(rect, _fill);

        var iconSize = (int)Math.Round(Math.Min(rect.Width, rect.Height - LabelHeight) * 0.5);
        if (iconSize > 8 && tile.Icon is { Length: > 0 } iconName &&
            _icons.Load(iconName, iconSize) is { } image)
        {
            var left = rect.MidX - (iconSize / 2f);
            var topEdge = rect.MidY - (iconSize / 2f) - (LabelHeight * 0.4f);
            canvas.DrawImage(
                image,
                new SKRect(left, topEdge, left + iconSize, topEdge + iconSize),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        _fill.Color = SKColors.White;
        var inset = 8f;
        var label = Fonts.Ellipsize(_label, tile.Name, rect.Width - (inset * 2));
        canvas.DrawText(
            label, rect.Left + inset, rect.Bottom - inset, SKTextAlign.Left, _label, _fill);

        if (tile.Peek is { Length: > 0 } peek)
        {
            canvas.DrawText(
                Fonts.Ellipsize(_label, peek, rect.Width - (inset * 2)),
                rect.Left + inset, rect.Top + inset + _label.Size, SKTextAlign.Left, _label, _fill);
        }

        if (tile.Badge is { Length: > 0 } badge)
        {
            canvas.DrawText(
                badge, rect.Right - inset, rect.Bottom - inset, SKTextAlign.Right, _badge, _fill);
        }

        if (tile.Selected || tile.Check.IsRunning)
        {
            var mark = 26f;
            var alpha = tile.Check.IsRunning ? Math.Clamp(tile.Check.Alpha, 0, 1) : tile.Selected ? 1f : 0f;
            _fill.Color = new SKColor(0xffffffff).WithAlpha((byte)(alpha * 255));
            var corner = new SKRect(rect.Right - mark, rect.Top, rect.Right, rect.Top + mark);
            canvas.DrawRect(corner, _fill);
            _fill.Color = new SKColor(tile.Color).WithAlpha((byte)(alpha * 255));
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(2f, mark * 0.12f),
                Color = _fill.Color,
            };
            canvas.DrawLine(
                corner.Left + (mark * 0.22f), corner.MidY, corner.MidX, corner.Bottom - (mark * 0.25f), stroke);
            canvas.DrawLine(
                corner.MidX, corner.Bottom - (mark * 0.25f),
                corner.Right - (mark * 0.18f), corner.Top + (mark * 0.28f), stroke);
        }

        canvas.Restore();
    }

    public void Dispose()
    {
        _pressFrame.Deformer = null;
        _press.Dispose();
        SkiaCensus.Release(_badge);
        SkiaCensus.Release(_header);
        SkiaCensus.Release(_label);
        SkiaCensus.Release(_fill);
        _chrome.Dispose();
    }
}
