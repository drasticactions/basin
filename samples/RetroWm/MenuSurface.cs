using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal sealed class MenuSurface : IDisposable
{
    public const int ItemCount = 5;

    private const int MenuBorder = 1;
    private const int ItemPaddingX = 10;
    private const int ItemPaddingY = 4;

    private static readonly string[] Labels = ["Move", "Size", "Icon", "Zoom", "Close"];

    private readonly ManagerSurface _surface;
    private readonly bool[] _enabled = new bool[ItemCount];
    private int _hovered = -1;
    private int _lastHovered = -2;
    private int _lastScale = -1;
    private uint _lastMask = uint.MaxValue;

    internal MenuSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm,
        int scale)
    {
        Output = output;
        for (var i = 0; i < ItemCount; i++)
        {
            _enabled[i] = true;
        }

        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "retro-wm-menu");
        SurfaceSize = Measure(Math.Max(scale, 1));
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.SetSize(SurfaceSize.Width, SurfaceSize.Height);
        _surface.Configured += wm.RequestManage;
    }

    public WmOutput Output { get; }

    public Point Origin { get; set; }

    public Size SurfaceSize { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public int Hovered => _hovered;

    private static int ItemHeight => (int)Math.Ceiling(Theme.FontSize) + (ItemPaddingY * 2);

    public void ApplyPosition()
    {
        _surface.SetMargin(Origin.Y, Origin.X);
        _surface.CommitInitial();
    }

    public void SetEnabled(SystemMenuItem item, bool enabled) => _enabled[(int)item] = enabled;

    public int? ItemAt(int x, int y)
    {
        if (x < MenuBorder || x >= SurfaceSize.Width - MenuBorder
            || y < MenuBorder || y >= SurfaceSize.Height - MenuBorder)
        {
            return null;
        }

        var index = (y - MenuBorder) / ItemHeight;
        return index < ItemCount ? index : null;
    }

    public bool UpdateHover(int x, int y)
    {
        var next = ItemAt(x, y) is { } index && _enabled[index] ? index : -1;
        if (next != _hovered)
        {
            _hovered = next;
            return true;
        }

        return false;
    }

    public bool ClearHover()
    {
        if (_hovered != -1)
        {
            _hovered = -1;
            return true;
        }

        return false;
    }

    public void MoveSelection(int delta)
    {
        for (var step = 1; step <= ItemCount; step++)
        {
            var index = ((_hovered + (delta * step)) % ItemCount + ItemCount) % ItemCount;
            if (_enabled[index])
            {
                _hovered = index;
                return;
            }
        }
    }

    public void SelectFirstEnabled()
    {
        for (var i = 0; i < ItemCount; i++)
        {
            if (_enabled[i])
            {
                _hovered = i;
                return;
            }
        }

        _hovered = -1;
    }

    public bool Render(int scale)
    {
        if (!_surface.IsConfigured)
        {
            return false;
        }

        scale = Math.Max(scale, 1);
        var mask = 0u;
        for (var i = 0; i < ItemCount; i++)
        {
            if (_enabled[i])
            {
                mask |= 1u << i;
            }
        }

        if (_hovered == _lastHovered && scale == _lastScale && mask == _lastMask)
        {
            return false;
        }

        var pixels = _surface.Prepare(SurfaceSize.Width, SurfaceSize.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var surface = _surface.CreateCanvas(pixels);
        if (surface is null)
        {
            return false;
        }

        Draw(surface.Canvas, scale);
        surface.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, SurfaceSize.Width, SurfaceSize.Height));
        _lastHovered = _hovered;
        _lastScale = scale;
        _lastMask = mask;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();

    private static Size Measure(int scale)
    {
        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        var longest = 0f;
        foreach (var label in Labels)
        {
            longest = Math.Max(longest, font.MeasureText(label) / scale);
        }

        var width = (2 * MenuBorder) + (2 * ItemPaddingX) + (int)Math.Ceiling(longest);
        var height = (2 * MenuBorder) + (ItemCount * ItemHeight);
        return new Size(width, height);
    }

    private void Draw(SKCanvas canvas, int scale)
    {
        var width = SurfaceSize.Width * scale;
        var height = SurfaceSize.Height * scale;
        var border = MenuBorder * scale;
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        paint.Color = Theme.Color(Theme.MenuBg);
        canvas.DrawRect(0, 0, width, height, paint);

        paint.Color = Theme.Color(Theme.WindowLine);
        canvas.DrawRect(0, 0, width, border, paint);
        canvas.DrawRect(0, height - border, width, border, paint);
        canvas.DrawRect(0, border, border, height - (2 * border), paint);
        canvas.DrawRect(width - border, border, border, height - (2 * border), paint);

        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        font.Subpixel = true;
        var metrics = font.Metrics;
        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;

        for (var i = 0; i < ItemCount; i++)
        {
            var rowY = border + (i * ItemHeight * scale);
            var rowHeight = ItemHeight * scale;
            var selected = i == _hovered && _enabled[i];
            if (selected)
            {
                paint.Color = Theme.Color(Theme.MenuHighlightBg);
                canvas.DrawRect(border, rowY, width - (2 * border), rowHeight, paint);
            }

            var color = !_enabled[i] ? Ega.DarkGray
                : selected ? Theme.MenuHighlightText
                : Theme.MenuText;
            var baseline = rowY + ((rowHeight - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
            textPaint.Color = Theme.Color(color);
            canvas.DrawText(Labels[i], ItemPaddingX * scale, baseline, SKTextAlign.Left, font, textPaint);
        }
    }
}
