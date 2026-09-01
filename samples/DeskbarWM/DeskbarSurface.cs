using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class DeskbarSurface : IDisposable
{
    private const int WindowIndent = 22;

    private readonly ManagerSurface _surface;
    private readonly List<BarRow> _rows = [];
    private readonly List<Rect> _rowRects = [];
    private DeskbarPlacement? _appliedPlacement;
    private bool _appliedHidden;
    private bool _appliedTopLayer = true;
    private HorizontalLayout _horizontal;
    private Rect _leafRect;
    private Rect _trayRect;
    private bool _isHorizontal;
    private Size _requestedSize;
    private Rect _laidOutFrame;
    private DeskbarPlacement? _laidOutPlacement;
    private string? _lastKey;
    private bool _committedInitial;

    internal DeskbarSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Top, "deskbar");
        _surface.SetExclusiveZone(0);
        _surface.Configured += wm.RequestManage;
    }

    internal sealed record ArrowHit(Team Team);

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public int Hovered { get; private set; } = -1;

    public DeskbarPlacement Placement { get; private set; } = DeskbarPlacement.Default;

    public Size ConfiguredSize => _surface.ConfiguredSize;

    public bool AutoHidden { get; set; }

    public TrayView Tray { get; } = new();

    public bool Raised { get; set; }

    public Rect FrameRect
    {
        get
        {
            var area = Output.Area;
            var size = _surface.ConfiguredSize;
            if (size.IsEmpty || _appliedPlacement is null || _requestedSize == default
                || (_requestedSize.Width > 0 && size.Width != _requestedSize.Width)
                || (_requestedSize.Height > 0 && size.Height != _requestedSize.Height))
            {
                return _laidOutPlacement == Placement ? _laidOutFrame : Rect.Empty;
            }

            var x = Placement.Side == BarSide.Left ? area.X : area.Right - size.Width;
            var y = Placement.End == BarEnd.Top ? area.Y : area.Bottom - size.Height;
            if (_isHorizontal && Placement.State == DeskbarState.Expando)
            {
                x = area.X;
            }

            if (!_isHorizontal && Placement.State != DeskbarState.Mini)
            {
                y = area.Y;
            }

            _laidOutFrame = new Rect(x, y, size.Width, size.Height);
            _laidOutPlacement = Placement;
            return _laidOutFrame;
        }
    }

    public bool UpdateHover(int x, int y)
    {
        var next = RowAt(x, y);
        if (next != Hovered)
        {
            Hovered = next;
            return true;
        }

        return false;
    }

    public void ClearHover() => Hovered = -1;

    public object? HitAt(int x, int y)
    {
        if (AutoHidden)
        {
            return null;
        }

        if (!_surface.ConfiguredSize.IsEmpty
            && DragHandle.For(_isHorizontal, _surface.ConfiguredSize).Contains(new Point(x, y)))
        {
            return "handle";
        }

        if (_leafRect.Contains(new Point(x, y)))
        {
            return "leaf";
        }

        if (Tray.AppletAt(new Point(x, y)) is { } applet)
        {
            return applet;
        }

        var row = RowAt(x, y);
        if (row < 0)
        {
            return null;
        }

        if (_rows[row] is TeamEntry team)
        {
            if (!_isHorizontal && x < _rowRects[row].X + 16 && HasArrow(team))
            {
                return new ArrowHit(team.Team);
            }

            return team.Team;
        }

        return _rows[row];
    }

    public bool Render(IReadOnlyList<BarRow> rows, int scale, Config config)
    {
        _rows.Clear();
        _rows.AddRange(rows);

        var placement = config.Placement;
        Placement = placement;
        _isHorizontal = placement.Orientation == BarOrientation.Horizontal;
        var hidden = config.AutoHide && AutoHidden;

        if (_appliedPlacement != placement || _appliedHidden != hidden)
        {
            _appliedPlacement = placement;
            _appliedHidden = hidden;
            _requestedSize = default;
            ApplyAnchors(placement, config, hidden);
        }

        var topLayer = true;
        if (!config.AlwaysOnTop && config.AutoRaise)
        {
            topLayer = Raised;
        }

        if (config.AutoHide)
        {
            topLayer = true;
        }

        if (_appliedTopLayer != topLayer)
        {
            _appliedTopLayer = topLayer;
            _surface.SetLayer(topLayer ? ZwlrLayerShellV1.Layer.Top : ZwlrLayerShellV1.Layer.Bottom);
        }

        using var font = new SKFont(Fonts.Sans, Theme.FontSize);
        var desired = DesiredSize(config, font, hidden);

        if (_requestedSize != desired)
        {
            _requestedSize = desired;
            _surface.SetSize(desired.Width, desired.Height);
            if (!_committedInitial)
            {
                _committedInitial = true;
                _surface.CommitInitial();
                return false;
            }

            _surface.Surface.Commit();
            return false;
        }

        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        LayoutRows(size, config, hidden);

        var key = RenderKey(size, scale, hidden);
        if (key == _lastKey)
        {
            return false;
        }

        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return false;
        }

        canvas.Canvas.Scale(scale);
        Draw(canvas.Canvas, size, config, font, hidden);
        canvas.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, size.Width, size.Height));
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Invalidate() => _lastKey = null;

    public void Dispose() => _surface.Dispose();

    private bool HasArrow(TeamEntry entry) =>
        entry.Team.Windows.Count > 0 && !_isHorizontal;

    private int RowAt(int x, int y)
    {
        if (AutoHidden)
        {
            return -1;
        }

        for (var i = 0; i < _rowRects.Count; i++)
        {
            if (_rowRects[i].Contains(new Point(x, y)))
            {
                return i;
            }
        }

        return -1;
    }

    private Size DesiredSize(Config config, SKFont font, bool hidden)
    {
        if (_isHorizontal)
        {
            var height = Placement.State == DeskbarState.Mini
                ? Math.Max(HorizontalLayout.MinHeight, Tray.PreferredHeight)
                : Math.Max(Math.Max(HorizontalLayout.MinHeight, config.IconSize + 8), Tray.PreferredHeight);
            var width = Placement.State == DeskbarState.Mini
                ? HorizontalLayout.LeafWidth + 1 + Tray.MeasureWidth(font, height) + DragHandle.Thickness
                : 0;
            return hidden ? new Size(width, 1) : new Size(width, height);
        }

        var barWidth = config.DeskbarWidth > 0
            ? config.DeskbarWidth
            : VerticalLayout.DefaultWidth(config.IconSize);
        var trayHeight = Tray.PreferredHeight;
        int barHeight;
        switch (Placement.State)
        {
            case DeskbarState.Mini:
                barHeight = VerticalLayout.MenuBarHeight + trayHeight + DragHandle.Thickness;
                break;
            case DeskbarState.Full:
                barHeight = 0;
                break;
            default:
                var content = VerticalLayout.MenuBarHeight + trayHeight;
                foreach (var row in _rows)
                {
                    content += RowHeight(row, config);
                }

                barHeight = content + VerticalLayout.Gutter + DragHandle.Thickness;
                break;
        }

        return hidden ? new Size(1, Math.Max(barHeight, VerticalLayout.MenuBarHeight)) : new Size(barWidth, barHeight);
    }

    private static int RowHeight(BarRow row, Config config) => row switch
    {
        WindowEntry => (int)MathF.Ceiling(Theme.FontSize) + 9,
        _ => Math.Max(config.IconSize + 8, (int)MathF.Ceiling(Theme.FontSize) + 10),
    };

    private void LayoutRows(Size size, Config config, bool hidden)
    {
        _rowRects.Clear();
        using var trayFont = new SKFont(Fonts.Sans, Theme.FontSize);
        if (hidden || Placement.State == DeskbarState.Mini)
        {
            if (_isHorizontal)
            {
                _leafRect = new Rect(0, 0, HorizontalLayout.LeafWidth, size.Height);
                _trayRect = new Rect(
                    _leafRect.Right + 1,
                    0,
                    Math.Max(size.Width - _leafRect.Right - 1 - DragHandle.Thickness, 0),
                    size.Height);
            }
            else
            {
                _leafRect = new Rect(0, 0, size.Width, VerticalLayout.MenuBarHeight);
                _trayRect = new Rect(0, _leafRect.Bottom, size.Width, hidden ? 0 : Tray.PreferredHeight);
            }

            Tray.Layout(hidden ? Rect.Empty : _trayRect, trayFont);
            foreach (var _ in _rows)
            {
                _rowRects.Add(Rect.Empty);
            }

            return;
        }

        if (_isHorizontal)
        {
            int natural;
            using (var font = new SKFont(Fonts.Sans, Theme.FontSize))
            {
                var widest = 0f;
                foreach (var row in _rows)
                {
                    if (row is TeamEntry team)
                    {
                        widest = MathF.Max(widest, font.MeasureText(team.Label));
                    }
                }

                natural = config.IconSize + 10 + (int)MathF.Ceiling(widest) + 12;
            }

            var teamCount = 0;
            foreach (var row in _rows)
            {
                if (row is TeamEntry)
                {
                    teamCount++;
                }
            }

            var trayWidth = Tray.MeasureWidth(trayFont, size.Height);
            _horizontal = HorizontalLayout.Compute(
                size.Width, config.IconSize, teamCount, natural, config.ShowLabels,
                trayWidth: DragHandle.Thickness + trayWidth);
            _leafRect = _horizontal.LeafRect;
            _trayRect = new Rect(
                size.Width - DragHandle.Thickness - trayWidth, 0, trayWidth, size.Height);
            Tray.Layout(_trayRect, trayFont);
            var index = 0;
            foreach (var row in _rows)
            {
                _rowRects.Add(row is TeamEntry ? _horizontal.ItemRect(index++) : Rect.Empty);
            }

            return;
        }

        _leafRect = new Rect(0, 0, size.Width, VerticalLayout.MenuBarHeight);
        _trayRect = new Rect(0, _leafRect.Bottom, size.Width, Tray.PreferredHeight);
        Tray.Layout(_trayRect, trayFont);
        var y = _trayRect.Bottom;
        foreach (var row in _rows)
        {
            var height = RowHeight(row, config);
            _rowRects.Add(new Rect(0, y, size.Width, height));
            y += height;
        }
    }

    private void ApplyAnchors(DeskbarPlacement placement, Config config, bool hidden)
    {
        var sideEdge = placement.Side == BarSide.Left
            ? ZwlrLayerSurfaceV1.Anchor.Left
            : ZwlrLayerSurfaceV1.Anchor.Right;
        var endEdge = placement.End == BarEnd.Top
            ? ZwlrLayerSurfaceV1.Anchor.Top
            : ZwlrLayerSurfaceV1.Anchor.Bottom;

        if (placement.State == DeskbarState.Mini)
        {
            _surface.SetAnchor(sideEdge | endEdge);
            _surface.SetExclusiveZone(0);
            return;
        }

        if (placement.Orientation == BarOrientation.Horizontal)
        {
            _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right | endEdge);
            _surface.SetExclusiveZone(
                hidden ? 0 : Math.Max(HorizontalLayout.MinHeight, config.IconSize + 8));
            return;
        }

        if (placement.State == DeskbarState.Full)
        {
            _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom | sideEdge);
            var width = config.DeskbarWidth > 0
                ? config.DeskbarWidth
                : VerticalLayout.DefaultWidth(config.IconSize);
            _surface.SetExclusiveZone(hidden ? 0 : width);
            return;
        }

        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | sideEdge);
        _surface.SetExclusiveZone(0);
    }

    private string RenderKey(Size size, int scale, bool hidden)
    {
        var key = $"{size.Width}x{size.Height}|{scale}|{Hovered}|{Theme.Flavor}|{Placement}|{hidden}|{Tray.RenderState}";
        foreach (var row in _rows)
        {
            key += row switch
            {
                TeamEntry team => $"|t:{team.Label}:{team.Active}:{team.Hidden}:{team.Expanded}:{team.Icon is not null}",
                WindowEntry window => $"|w:{window.Label}:{window.Active}:{window.Hidden}",
                _ => "|?",
            };
        }

        return key;
    }

    private void Draw(SKCanvas canvas, Size size, Config config, SKFont font, bool hidden)
    {
        var panel = new SKColor(216, 216, 216);
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        canvas.Clear(SKColors.Transparent);
        paint.Color = panel;
        canvas.DrawRect(0, 0, size.Width, size.Height, paint);
        if (hidden)
        {
            return;
        }

        DrawPanelBevel(canvas, paint, size, panel);
        DrawLeaf(canvas, paint, _leafRect, panel);
        if (_isHorizontal && Placement.State != DeskbarState.Mini)
        {
            paint.Color = Theme.Tint(panel, Theme.Darken2);
            canvas.DrawRect(_leafRect.Right, 1, 1, size.Height - 2, paint);
        }

        if (!_trayRect.IsEmpty)
        {
            if (!_isHorizontal)
            {
                paint.Color = Theme.Tint(panel, Theme.Darken2);
                canvas.DrawRect(_trayRect.X + 1, _trayRect.Bottom - 1, _trayRect.Width - 2, 1, paint);
            }

            Tray.Draw(canvas, paint, font);
        }

        if (Placement.State != DeskbarState.Mini)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rowRects[i].IsEmpty)
                {
                    continue;
                }

                switch (_rows[i])
                {
                    case TeamEntry team:
                        DrawTeamRow(canvas, paint, font, _rowRects[i], team, i == Hovered, config, panel);
                        break;
                    case WindowEntry window:
                        DrawWindowRow(canvas, paint, font, _rowRects[i], window, i == Hovered, panel);
                        break;
                }
            }
        }

        DrawHandle(canvas, size, panel);
    }

    private void DrawHandle(SKCanvas canvas, Size size, SKColor panel)
    {
        var rect = DragHandle.For(_isHorizontal, size);
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = panel;
        canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, paint);
        if (_isHorizontal)
        {
            var x = rect.X + 2;
            paint.Color = Theme.Tint(panel, Theme.Lighten2);
            canvas.DrawRect(x, rect.Y + 3, 1, rect.Height - 6, paint);
            paint.Color = Theme.Tint(panel, Theme.Darken2);
            canvas.DrawRect(x + 1, rect.Y + 3, 1, rect.Height - 6, paint);
        }
        else
        {
            var y = rect.Y + 2;
            paint.Color = Theme.Tint(panel, Theme.Lighten2);
            canvas.DrawRect(rect.X + 3, y, rect.Width - 6, 1, paint);
            paint.Color = Theme.Tint(panel, Theme.Darken2);
            canvas.DrawRect(rect.X + 3, y + 1, rect.Width - 6, 1, paint);
        }
    }

    private static void DrawPanelBevel(SKCanvas canvas, SKPaint paint, Size size, SKColor panel)
    {
        paint.Color = Theme.Tint(panel, Theme.Lighten2);
        canvas.DrawRect(0, 0, size.Width, 1, paint);
        canvas.DrawRect(0, 0, 1, size.Height, paint);
        paint.Color = Theme.Tint(panel, Theme.Darken2);
        canvas.DrawRect(0, size.Height - 1, size.Width, 1, paint);
        canvas.DrawRect(size.Width - 1, 0, 1, size.Height, paint);
    }

    private static void DrawLeaf(SKCanvas canvas, SKPaint paint, Rect rect, SKColor panel)
    {
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(rect.X, rect.Y),
            new SKPoint(rect.X, rect.Bottom),
            [Theme.Tint(panel, Theme.LightenHalf), panel],
            SKShaderTileMode.Clamp);
        paint.Shader = gradient;
        canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, paint);
        paint.Shader = null;

        paint.Color = Theme.Tint(panel, Theme.Darken2);
        canvas.DrawRect(rect.X, rect.Bottom - 1, rect.Width, 1, paint);

        paint.IsAntialias = true;
        paint.Color = new SKColor(51, 102, 152);
        var builder = new SKPathBuilder();
        var cx = rect.X + 12f;
        var cy = rect.Y + (rect.Height / 2f);
        builder.MoveTo(cx - 5, cy + 7);
        builder.CubicTo(cx - 6, cy - 2, cx - 1, cy - 8, cx + 7, cy - 8);
        builder.CubicTo(cx + 6, cy - 1, cx + 3, cy + 5, cx - 3, cy + 6);
        builder.CubicTo(cx - 4, cy + 7, cx - 5, cy + 8, cx - 5, cy + 7);
        builder.Close();
        using var leaf = builder.Detach();
        canvas.DrawPath(leaf, paint);
        paint.IsAntialias = false;
    }

    private void DrawTeamRow(
        SKCanvas canvas,
        SKPaint paint,
        SKFont font,
        Rect rect,
        TeamEntry entry,
        bool hovered,
        Config config,
        SKColor panel)
    {
        if (entry.Active || hovered)
        {
            paint.Color = entry.Active
                ? Theme.Tint(panel, Theme.Darken1)
                : Theme.Tint(panel, Theme.DarkenHalf);
            canvas.DrawRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2, paint);
        }

        var x = rect.X + VerticalLayout.Gutter;
        if (!_isHorizontal && config.ExpandWindows && HasArrow(entry))
        {
            paint.IsAntialias = true;
            paint.Color = new SKColor(80, 80, 80);
            var builder = new SKPathBuilder();
            var cy = rect.Y + (rect.Height / 2f);
            if (entry.Expanded)
            {
                builder.MoveTo(x, cy - 1);
                builder.LineTo(x + 8, cy - 1);
                builder.LineTo(x + 4, cy + 4);
            }
            else
            {
                builder.MoveTo(x + 1, cy - 4);
                builder.LineTo(x + 6, cy);
                builder.LineTo(x + 1, cy + 4);
            }

            builder.Close();
            using var arrow = builder.Detach();
            canvas.DrawPath(arrow, paint);
            paint.IsAntialias = false;
            x += 11;
        }

        var iconSize = Math.Min(config.IconSize, rect.Height - 4);
        var iconY = rect.Y + ((rect.Height - iconSize) / 2);
        if (entry.Icon is { } icon)
        {
            using var iconPaint = new SKPaint();
            iconPaint.Color = entry.Hidden ? SKColors.White.WithAlpha(128) : SKColors.White;
            canvas.DrawImage(
                icon,
                new SKRect(x, iconY, x + iconSize, iconY + iconSize),
                new SKSamplingOptions(SKFilterMode.Linear),
                iconPaint);
        }

        if (!config.ShowLabels)
        {
            return;
        }

        var textX = x + iconSize + 6;
        DrawRowLabel(canvas, paint, font, rect, entry.Label, entry.Hidden, textX);
    }

    private void DrawWindowRow(
        SKCanvas canvas,
        SKPaint paint,
        SKFont font,
        Rect rect,
        WindowEntry entry,
        bool hovered,
        SKColor panel)
    {
        if (entry.Active || hovered)
        {
            paint.Color = entry.Active
                ? Theme.Tint(panel, Theme.Darken1)
                : Theme.Tint(panel, Theme.DarkenHalf);
            canvas.DrawRect(rect.X + 1, rect.Y, rect.Width - 2, rect.Height, paint);
        }

        DrawRowLabel(canvas, paint, font, rect, entry.Label, entry.Hidden, rect.X + WindowIndent);
    }

    private static void DrawRowLabel(
        SKCanvas canvas,
        SKPaint paint,
        SKFont font,
        Rect rect,
        string label,
        bool dimmed,
        int textX)
    {
        var available = rect.Right - VerticalLayout.Gutter - textX;
        if (available <= 0)
        {
            return;
        }

        var text = Fonts.Ellipsize(font, label, available);
        var metrics = font.Metrics;
        var baseline = rect.Y + ((rect.Height - metrics.Ascent - metrics.Descent) / 2f);
        paint.Color = dimmed ? new SKColor(96, 96, 96) : SKColors.Black;
        paint.IsAntialias = true;
        canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, paint);
        paint.IsAntialias = false;
    }
}
