using Basin.WindowManager;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class TitlebarSurface : IDisposable
{
    private const int ButtonGap = 1;

    private readonly WlSurface _surface;
    private readonly WmDecoration _decoration;
    private readonly ShmSlots _slots;
    private readonly OutputScales _scales;
    private readonly HashSet<WlOutput> _entered = [];

    private int _width;
    private int _height;
    private int _bufferWidth;
    private int _bufferHeight;
    private int _contentWidth;
    private int _contentHeight;
    private int _borderWidth = Theme.BorderWidth;
    private int _scale = 1;
    private bool _dirty = true;
    private bool _needsFullDamage = true;
    private bool _disposed;

    private string? _lastTitle;
    private bool _lastActive;
    private bool _lastMaximized;
    private bool _lastShowMinimize;
    private bool _lastShowMaximize;
    private FrameStyle _lastStyle;
    private TitlebarButton? _lastHovered;
    private bool _lastLeftDown;

    internal TitlebarSurface(WmWindow window, WlCompositor compositor, WlShm shm, OutputScales scales)
    {
        _scales = scales;
        _surface = compositor.CreateSurface();
        _surface.Enter += (_, e) =>
        {
            if (e.Output is not null)
            {
                _entered.Add(e.Output);
            }
        };
        _surface.Leave += (_, e) =>
        {
            if (e.Output is not null)
            {
                _entered.Remove(e.Output);
            }
        };
        _decoration = window.CreateDecorationAbove(_surface);
        _slots = new ShmSlots(shm);
    }

    public bool Mapped { get; private set; }

    public uint SurfaceId => _surface.Id;

    public TitlebarButton? ButtonAt(int contentWidth, int borderWidth, int localX, int localY, bool showMinimize, bool showMaximize)
    {
        if (contentWidth <= 0)
        {
            return null;
        }

        var relX = localX - borderWidth;
        var relY = localY - borderWidth;
        if (relX < 0 || relY < 0 || relX >= contentWidth || relY >= Theme.TitlebarHeight)
        {
            return null;
        }

        var (close, hide, max) = ButtonRects(contentWidth, showMinimize, showMaximize);
        if (close.Contains(new Point(relX, relY)))
        {
            return TitlebarButton.Close;
        }

        if (hide is { } hideRect && hideRect.Contains(new Point(relX, relY)))
        {
            return TitlebarButton.Hide;
        }

        if (max is { } maxRect && maxRect.Contains(new Point(relX, relY)))
        {
            return TitlebarButton.Maximize;
        }

        return null;
    }

    public int ScaleFor(uint fallbackOutputName)
    {
        if (_entered.Count == 0)
        {
            return _scales.ScaleForName(fallbackOutputName);
        }

        var scale = 1;
        foreach (var output in _entered)
        {
            scale = Math.Max(scale, _scales.ScaleFor(output));
        }

        return scale;
    }

    public void EnsureBuffer(int contentWidth, int contentHeight, int scale, FrameStyle style)
    {
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        scale = Math.Max(scale, 1);
        var borderWidth = Theme.BorderWidthFor(style);
        _borderWidth = borderWidth;
        var width = contentWidth + (borderWidth * 2);
        var height = contentHeight + Theme.TitlebarHeight + (borderWidth * 2);
        var bufferWidth = width * scale;
        var bufferHeight = height * scale;
        if (bufferWidth <= 0 || bufferHeight <= 0)
        {
            return;
        }

        if (_width != width || _height != height
            || _bufferWidth != bufferWidth || _bufferHeight != bufferHeight || _scale != scale)
        {
            _width = width;
            _height = height;
            _bufferWidth = bufferWidth;
            _bufferHeight = bufferHeight;
            _scale = scale;
            _dirty = true;
            _needsFullDamage = true;
        }

        _contentWidth = contentWidth;
        _contentHeight = contentHeight;
        _surface.SetBufferScale(scale);
    }

    public void UpdateInputRegion(WlCompositor compositor)
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        var region = compositor.CreateRegion();
        region.Add(0, 0, _width, _height);
        if (_contentWidth > 0 && _contentHeight > 0)
        {
            region.Subtract(
                _borderWidth,
                _borderWidth + Theme.TitlebarHeight,
                _contentWidth,
                _contentHeight);
        }

        _surface.SetInputRegion(region);
        region.Destroy();
    }

    public bool Render(
        string? title,
        bool active,
        bool maximized,
        bool showMinimize,
        bool showMaximize,
        FrameStyle style,
        TitlebarButton? hovered,
        bool leftDown)
    {
        var changed = _lastTitle != title
            || _lastActive != active
            || _lastMaximized != maximized
            || _lastShowMinimize != showMinimize
            || _lastShowMaximize != showMaximize
            || _lastStyle != style
            || _lastHovered != hovered
            || _lastLeftDown != leftDown;
        if (changed)
        {
            _lastTitle = title;
            _lastActive = active;
            _lastMaximized = maximized;
            _lastShowMinimize = showMinimize;
            _lastShowMaximize = showMaximize;
            _lastStyle = style;
            _lastHovered = hovered;
            _lastLeftDown = leftDown;
            _dirty = true;
        }

        if (!_dirty || _width <= 0 || _height <= 0 || _contentWidth <= 0 || _contentHeight <= 0)
        {
            return false;
        }

        var stride = _bufferWidth * 4;
        var pixels = _slots.Prepare(_bufferWidth, _bufferHeight, stride);
        if (pixels == 0)
        {
            return false;
        }

        _slots.CurrentBytes().Clear();

        var info = new SKImageInfo(_bufferWidth, _bufferHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, pixels, stride);
        if (surface is null)
        {
            return false;
        }

        Draw(surface.Canvas, title, active, maximized, showMinimize, showMaximize, style, hovered, leftDown);
        surface.Canvas.Flush();
        _dirty = false;
        return true;
    }

    public void SetOffset(int swallowTop) =>
        _decoration.SetOffset(-_borderWidth, -_borderWidth - Theme.TitlebarHeight + swallowTop);

    public void Invalidate()
    {
        _dirty = true;
        _needsFullDamage = true;
    }

    public void SyncNextCommit() => _decoration.SyncNextCommit();

    public void Commit()
    {
        if (_slots.CurrentBuffer is not { } buffer)
        {
            return;
        }

        _surface.Attach(buffer, 0, 0);
        if (_needsFullDamage)
        {
            _surface.DamageBuffer(0, 0, _bufferWidth, _bufferHeight);
            _needsFullDamage = false;
        }
        else
        {
            DamageFrame();
        }

        _surface.Commit();
        _slots.MarkAttached();
        Mapped = true;
    }

    public void Unmap()
    {
        _surface.Attach(null, 0, 0);
        _surface.Commit();
        Mapped = false;
        _dirty = true;
        _needsFullDamage = true;
        _lastTitle = null;
        _lastActive = false;
        _lastMaximized = false;
        _lastHovered = null;
        _lastLeftDown = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _slots.Dispose();

        _decoration.Dispose();
        if (!_surface.IsDestroyed)
        {
            _surface.Destroy();
        }
    }

    public static (Rect Close, Rect? Hide, Rect? Maximize) ButtonRects(
        int contentWidth,
        bool showMinimize,
        bool showMaximize)
    {
        var size = Theme.TitlebarHeight;
        var close = new Rect(0, 0, size, size);

        var rightX = contentWidth - size;
        Rect? maximize = null;
        if (showMaximize)
        {
            maximize = new Rect(Math.Max(rightX, 0), 0, size, size);
            rightX -= size + ButtonGap;
        }

        Rect? hide = showMinimize ? new Rect(Math.Max(rightX, 0), 0, size, size) : null;
        return (close, hide, maximize);
    }

    private void DamageFrame()
    {
        var bw = Math.Max(_borderWidth, 0);
        var scale = Math.Max(_scale, 1);

        var cutX0 = bw * scale;
        var cutY0 = (bw + Theme.TitlebarHeight + 1) * scale;
        var cutX1 = _bufferWidth - (bw * scale);
        var cutY1 = _bufferHeight - (bw * scale);

        if (cutX1 <= cutX0 || cutY1 <= cutY0)
        {
            _surface.DamageBuffer(0, 0, _bufferWidth, _bufferHeight);
            return;
        }

        Damage(0, 0, _bufferWidth, cutY0);
        Damage(0, cutY1, _bufferWidth, _bufferHeight - cutY1);
        Damage(0, cutY0, cutX0, cutY1 - cutY0);
        Damage(cutX1, cutY0, _bufferWidth - cutX1, cutY1 - cutY0);
    }

    private void Damage(int x, int y, int width, int height)
    {
        if (width > 0 && height > 0)
        {
            _surface.DamageBuffer(x, y, width, height);
        }
    }

    private void Draw(
        SKCanvas canvas,
        string? title,
        bool active,
        bool maximized,
        bool showMinimize,
        bool showMaximize,
        FrameStyle style,
        TitlebarButton? hovered,
        bool leftDown)
    {
        var scale = Math.Max(_scale, 1);
        var borderOuter = active ? Theme.BorderActiveOuter : Theme.BorderInactiveOuter;
        var borderMid = active ? Theme.BorderActiveMid : Theme.BorderInactiveMid;
        var borderInner = active ? Theme.BorderActiveInner : Theme.BorderInactiveInner;
        var titlebarBg = active ? Theme.TitlebarBgActive : Theme.TitlebarBgInactive;
        if (style == FrameStyle.Dialog)
        {
            borderMid = titlebarBg;
        }

        var bw = Theme.BorderWidthFor(style);
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        DrawBorderLayer(canvas, paint, 0, 1 * scale, borderOuter);
        if (style != FrameStyle.FixedSize)
        {
            var midWidth = Math.Max(bw - 2, 0);
            DrawBorderLayer(canvas, paint, 1 * scale, midWidth * scale, borderMid);
            DrawBorderLayer(canvas, paint, (1 + midWidth) * scale, 1 * scale, borderInner);
        }

        var titleHeight = Math.Min(Theme.TitlebarHeight, _height - (bw * 2));
        if (titleHeight <= 0)
        {
            return;
        }

        FillRect(canvas, paint, bw * scale, bw * scale, _contentWidth * scale, titleHeight * scale, titlebarBg);

        var (close, hide, max) = ButtonRects(_contentWidth, showMinimize, showMaximize);
        var titleHeightPx = titleHeight * scale;
        var unit = Math.Max(titleHeightPx / Math.Max(Theme.TitlebarHeight, 1), 1);
        DrawSeparator(canvas, paint, (bw + close.X + close.Width) * scale, bw * scale, unit, titleHeightPx, borderOuter);
        if (hide is { } hideRect)
        {
            DrawSeparator(canvas, paint, (bw + hideRect.X - 1) * scale, bw * scale, unit, titleHeightPx, borderOuter);
        }

        if (max is { } maxRect)
        {
            DrawSeparator(canvas, paint, (bw + maxRect.X - 1) * scale, bw * scale, unit, titleHeightPx, borderOuter);
        }

        var separatorY = bw + titleHeight;
        if (separatorY < _height - bw)
        {
            FillRect(canvas, paint, bw * scale, separatorY * scale, _contentWidth * scale, scale, borderOuter);
        }

        var pressed = leftDown ? hovered : null;
        var iconSize = Math.Clamp(Theme.TitlebarHeight - 4, 6, Math.Max(Theme.TitlebarHeight, 1));
        var buttonSize = Theme.TitlebarHeight * scale;

        var closeBg = pressed == TitlebarButton.Close ? Theme.ButtonBgPressedClose : Theme.ButtonBg;
        FillRect(canvas, paint, (bw + close.X) * scale, (bw + close.Y) * scale, buttonSize, buttonSize, closeBg);
        DrawCloseIcon(
            canvas,
            (bw + close.X + ((close.Width - iconSize) / 2)) * scale,
            (bw + close.Y + ((close.Height - iconSize) / 2)) * scale,
            iconSize * scale);

        if (hide is { } hideButton)
        {
            var hidePressed = pressed == TitlebarButton.Hide;
            var offset = hidePressed ? scale : 0;
            DrawButtonBevel(canvas, paint, (bw + hideButton.X) * scale, (bw + hideButton.Y) * scale, buttonSize, hidePressed);
            DrawCaretIcon(
                canvas,
                ((bw + hideButton.X + ((hideButton.Width - iconSize) / 2)) * scale) + offset,
                ((bw + hideButton.Y + ((hideButton.Height - iconSize) / 2)) * scale) + offset,
                iconSize * scale,
                down: true);
        }

        if (max is { } maxButton)
        {
            var maxPressed = pressed == TitlebarButton.Maximize;
            var offset = maxPressed ? scale : 0;
            DrawButtonBevel(canvas, paint, (bw + maxButton.X) * scale, (bw + maxButton.Y) * scale, buttonSize, maxPressed);
            var iconX = ((bw + maxButton.X + ((maxButton.Width - iconSize) / 2)) * scale) + offset;
            var iconY = ((bw + maxButton.Y + ((maxButton.Height - iconSize) / 2)) * scale) + offset;
            if (maximized)
            {
                DrawRestoreIcon(canvas, iconX, iconY, iconSize * scale);
            }
            else
            {
                DrawCaretIcon(canvas, iconX, iconY, iconSize * scale, down: false);
            }
        }

        if (!string.IsNullOrEmpty(title))
        {
            var textStart = Math.Max(close.X + close.Width + ButtonGap, 0);
            var textPadding = (int)Math.Round(Theme.FontSize * 0.5);
            var rightX = hide?.X ?? max?.X ?? _contentWidth;
            var textEnd = Math.Min(rightX - ButtonGap - textPadding, _contentWidth);
            var textWidth = Math.Max(textEnd - textStart, 0);
            if (textWidth > 0)
            {
                DrawTitle(
                    canvas,
                    title,
                    (bw + textStart) * scale,
                    bw * scale,
                    textWidth * scale,
                    titleHeightPx,
                    textPadding * scale,
                    active ? Theme.TitlebarTextActive : Theme.TitlebarTextInactive,
                    scale);
            }
        }
    }

    private void DrawBorderLayer(SKCanvas canvas, SKPaint paint, int offset, int thickness, uint color)
    {
        if (thickness <= 0)
        {
            return;
        }

        var width = _bufferWidth - (offset * 2);
        var height = _bufferHeight - (offset * 2);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        FillRect(canvas, paint, offset, offset, width, thickness, color);
        FillRect(canvas, paint, offset, offset + height - thickness, width, thickness, color);
        FillRect(canvas, paint, offset, offset + thickness, thickness, height - (thickness * 2), color);
        FillRect(canvas, paint, offset + width - thickness, offset + thickness, thickness, height - (thickness * 2), color);
    }

    private static void FillRect(SKCanvas canvas, SKPaint paint, float x, float y, float width, float height, uint color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        paint.Color = Theme.Color(color);
        canvas.DrawRect(x, y, width, height, paint);
    }

    private static void DrawSeparator(SKCanvas canvas, SKPaint paint, int x, int y, int unit, int height, uint color)
    {
        if (x >= 0)
        {
            FillRect(canvas, paint, x, y, unit, height, color);
        }
    }

    private static void DrawButtonBevel(SKCanvas canvas, SKPaint paint, int x, int y, int size, bool pressed)
    {
        var unit = Math.Max(size / Math.Max(Theme.TitlebarHeight, 1), 1);
        var highlight = pressed ? Theme.ButtonShadow : Theme.ButtonHighlight;
        var shadow = pressed ? Theme.ButtonBg : Theme.ButtonShadow;

        FillRect(canvas, paint, x, y, size, size, Theme.ButtonBg);
        FillRect(canvas, paint, x, y, size, unit, highlight);
        FillRect(canvas, paint, x, y, unit, size, highlight);
        if (pressed)
        {
            return;
        }

        if (size >= 3 * unit)
        {
            FillRect(canvas, paint, x + unit, y + size - (2 * unit), size - (2 * unit), unit, shadow);
            FillRect(canvas, paint, x + size - (2 * unit), y + unit, unit, size - (2 * unit), shadow);
        }

        FillRect(canvas, paint, x, y + size - unit, size, unit, shadow);
        FillRect(canvas, paint, x + size - unit, y, unit, size, shadow);
    }

    private static void DrawCloseIcon(SKCanvas canvas, float x, float y, float size)
    {
        var u = size / 16f;
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = new SKColor(0x80, 0x80, 0x80);
        canvas.DrawRect(x + (2 * u), y + (7 * u), 13 * u, 3 * u, paint);
        paint.Color = SKColors.Black;
        canvas.DrawRect(x + (1 * u), y + (6 * u), 13 * u, 3 * u, paint);
        paint.Color = SKColors.White;
        canvas.DrawRect(x + (2 * u), y + (7 * u), 11 * u, 1 * u, paint);
    }

    private static void DrawCaretIcon(SKCanvas canvas, float x, float y, float size, bool down)
    {
        var u = size / 16f;
        using var builder = new SKPathBuilder();
        if (down)
        {
            builder.MoveTo(x + (4 * u), y + (6 * u));
            builder.LineTo(x + (4 * u), y + (6.5f * u));
            builder.LineTo(x + (8 * u), y + (10.5f * u));
            builder.LineTo(x + (12 * u), y + (6.5f * u));
            builder.LineTo(x + (12 * u), y + (6 * u));
        }
        else
        {
            builder.MoveTo(x + (4 * u), y + (10 * u));
            builder.LineTo(x + (4 * u), y + (9.5f * u));
            builder.LineTo(x + (8 * u), y + (5.5f * u));
            builder.LineTo(x + (12 * u), y + (9.5f * u));
            builder.LineTo(x + (12 * u), y + (10 * u));
        }

        builder.Close();
        using var path = builder.Detach();
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new SKColor(0x1a, 0x1a, 0x1a);
        canvas.DrawPath(path, paint);
    }

    private static void DrawRestoreIcon(SKCanvas canvas, float x, float y, float size)
    {
        var u = size / 16f;
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = new SKColor(0x1a, 0x1a, 0x1a);

        using var upBuilder = new SKPathBuilder();
        upBuilder.MoveTo(x + (4 * u), y + (7.25f * u));
        upBuilder.LineTo(x + (4 * u), y + (6.75f * u));
        upBuilder.LineTo(x + (8 * u), y + (2.75f * u));
        upBuilder.LineTo(x + (12 * u), y + (6.75f * u));
        upBuilder.LineTo(x + (12 * u), y + (7.25f * u));
        upBuilder.Close();
        using var up = upBuilder.Detach();
        canvas.DrawPath(up, paint);

        using var downBuilder = new SKPathBuilder();
        downBuilder.MoveTo(x + (4 * u), y + (8.75f * u));
        downBuilder.LineTo(x + (4 * u), y + (9.25f * u));
        downBuilder.LineTo(x + (8 * u), y + (13.25f * u));
        downBuilder.LineTo(x + (12 * u), y + (9.25f * u));
        downBuilder.LineTo(x + (12 * u), y + (8.75f * u));
        downBuilder.Close();
        using var downPath = downBuilder.Detach();
        canvas.DrawPath(downPath, paint);
    }

    private static void DrawTitle(
        SKCanvas canvas,
        string title,
        int x,
        int y,
        int width,
        int height,
        int padding,
        uint color,
        int scale)
    {
        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        var available = width - (padding * 2);
        if (available <= 0)
        {
            return;
        }

        var fit = (int)font.BreakText(title, available);
        if (fit <= 0)
        {
            return;
        }

        var metrics = font.Metrics;
        var baseline = y + ((height - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent + 1;
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = Theme.Color(color);
        canvas.DrawText(title[..fit], x + padding, baseline, SKTextAlign.Left, font, paint);
    }
}
