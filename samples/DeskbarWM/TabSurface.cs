using Basin.WindowManager;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class TabSurface : IDisposable
{
    private readonly WlSurface _surface;
    private readonly WmDecoration _decoration;
    private readonly ShmSlots _slots;
    private readonly OutputScales _scales;
    private readonly HashSet<WlOutput> _entered = [];

    private TabMetrics _metrics;
    private int _contentWidth;
    private int _contentHeight;
    private int _bufferWidth;
    private int _bufferHeight;
    private int _scale = 1;
    private bool _dirty = true;
    private bool _disposed;

    private string? _lastTitle;
    private bool _lastActive;
    private FramePart? _lastPressed;
    private LookFlavor _lastFlavor;
    private Rect _lastTabRect;
    private string? _lastStrip;

    internal TabSurface(WmWindow window, WlCompositor compositor, WlShm shm, OutputScales scales)
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

    public TabMetrics Metrics => _metrics;

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

    public void EnsureBuffer(int contentWidth, int contentHeight, int scale, TabMetrics metrics)
    {
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        scale = Math.Max(scale, 1);
        var bufferWidth = metrics.FrameWidth * scale;
        var bufferHeight = metrics.FrameHeight * scale;
        if (bufferWidth <= 0 || bufferHeight <= 0)
        {
            return;
        }

        if (_bufferWidth != bufferWidth || _bufferHeight != bufferHeight || _scale != scale
            || _metrics.BorderWidth != metrics.BorderWidth || _metrics.TabHeight != metrics.TabHeight)
        {
            _bufferWidth = bufferWidth;
            _bufferHeight = bufferHeight;
            _scale = scale;
            _dirty = true;
        }

        _metrics = metrics;
        _contentWidth = contentWidth;
        _contentHeight = contentHeight;
        _surface.SetBufferScale(scale);
    }

    public void UpdateInputRegion(WlCompositor compositor, int stripCount = 0)
    {
        if (_metrics.FrameWidth <= 0 || _metrics.FrameHeight <= 0)
        {
            return;
        }

        var region = compositor.CreateRegion();
        if (stripCount > 1)
        {
            region.Add(0, 0, _metrics.FrameWidth, _metrics.TabHeight);
        }
        else
        {
            region.Add(_metrics.TabRect.X, 0, _metrics.TabRect.Width, _metrics.TabRect.Height);
        }

        region.Add(0, _metrics.TabHeight, _metrics.FrameWidth, _metrics.FrameHeight - _metrics.TabHeight);
        if (_contentWidth > 0 && _contentHeight > 0)
        {
            region.Subtract(
                _metrics.BorderWidth,
                _metrics.TabHeight + _metrics.BorderWidth,
                _contentWidth,
                _contentHeight);
        }

        _surface.SetInputRegion(region);
        region.Destroy();
    }

    public bool Render(
        string? title,
        bool active,
        FramePart? pressed,
        WindowFeel feel,
        IReadOnlyList<(string Title, bool Front)>? strip = null)
    {
        var flavour = Theme.Flavor;
        var stripKey = strip is null ? null : string.Join("|", strip.Select(static s => $"{s.Title}:{s.Front}"));
        var changed = _lastTitle != title
            || _lastActive != active
            || _lastPressed != pressed
            || _lastFlavor != flavour
            || _lastTabRect != _metrics.TabRect
            || _lastStrip != stripKey;
        if (changed)
        {
            _lastTitle = title;
            _lastActive = active;
            _lastPressed = pressed;
            _lastFlavor = flavour;
            _lastTabRect = _metrics.TabRect;
            _lastStrip = stripKey;
            _dirty = true;
        }

        if (!_dirty || _bufferWidth <= 0 || _bufferHeight <= 0)
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

        surface.Canvas.Scale(_scale);
        Draw(surface.Canvas, title, active, pressed, feel, strip);
        surface.Canvas.Flush();
        _dirty = false;
        return true;
    }

    public void SetOffset() =>
        _decoration.SetOffset(-_metrics.BorderWidth, -_metrics.BorderWidth - _metrics.TabHeight);

    public void Invalidate() => _dirty = true;

    public void SyncNextCommit() => _decoration.SyncNextCommit();

    public void Commit()
    {
        if (_slots.CurrentBuffer is not { } buffer)
        {
            return;
        }

        _surface.Attach(buffer, 0, 0);
        _surface.DamageBuffer(0, 0, _bufferWidth, _bufferHeight);
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
        _lastTitle = null;
        _lastActive = false;
        _lastPressed = null;
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

    private void Draw(
        SKCanvas canvas,
        string? title,
        bool active,
        FramePart? pressed,
        WindowFeel feel,
        IReadOnlyList<(string Title, bool Front)>? strip)
    {
        var m = _metrics;
        var frame = Theme.FrameColor(active);
        var floating = feel == WindowFeel.Floating;

        Span<SKColor> border = stackalloc SKColor[6];
        border[0] = Theme.Tint(frame, Theme.Darken2);
        border[1] = Theme.Tint(frame, Theme.Lighten2);
        border[2] = frame;
        border[3] = Theme.Tint(frame, Theme.DarkenHalf);
        border[4] = Theme.Tint(frame, Theme.Darken2);
        border[5] = Theme.Tint(frame, Theme.Darken3);

        using var paint = new SKPaint();
        paint.IsAntialias = false;

        DrawBorders(canvas, paint, border, floating);
        if (strip is { Count: > 1 })
        {
            DrawStrip(canvas, paint, strip, active, pressed);
        }
        else
        {
            DrawTab(canvas, paint, border, title, active, pressed);
        }

        DrawResizeCorner(canvas, paint, border);
    }

    private void DrawStrip(
        SKCanvas canvas,
        SKPaint paint,
        IReadOnlyList<(string Title, bool Front)> strip,
        bool active,
        FramePart? pressed)
    {
        var m = _metrics;
        for (var i = 0; i < strip.Count; i++)
        {
            var slot = TabStrip.Slot(m.FrameWidth, m.TabHeight, strip.Count, i);
            var (slotTitle, front) = strip[i];
            var slotActive = front && active;
            var baseColor = Theme.TabColor(slotActive);
            var frameColor = Theme.FrameColor(slotActive);

            paint.Color = Theme.Tint(frameColor, Theme.Darken2);
            canvas.DrawRect(slot.X, slot.Y, 1, slot.Height, paint);
            canvas.DrawRect(slot.X, slot.Y, slot.Width, 1, paint);
            paint.Color = Theme.Tint(frameColor, Theme.Darken3);
            canvas.DrawRect(slot.Right - 1, slot.Y, 1, slot.Height, paint);

            if (Theme.Flavor == LookFlavor.Haiku)
            {
                using var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(slot.X, slot.Y),
                    new SKPoint(slot.X, slot.Bottom),
                    [Theme.Tint(baseColor, Theme.LightenHalf), baseColor],
                    SKShaderTileMode.Clamp);
                paint.Shader = gradient;
                canvas.DrawRect(slot.X + 1, slot.Y + 1, slot.Width - 2, slot.Height - 1, paint);
                paint.Shader = null;
            }
            else
            {
                paint.Color = baseColor;
                canvas.DrawRect(slot.X + 1, slot.Y + 1, slot.Width - 2, slot.Height - 1, paint);
            }

            var textX = slot.X + 6;
            var textRight = slot.Right - 6;
            if (front && !m.CloseRect.IsEmpty)
            {
                var close = TabStrip.CloseRect(slot, m);
                DrawCloseBox(canvas, paint, close, baseColor, pressed == FramePart.CloseBox);
                textX = close.Right + 5;
                var zoom = TabStrip.ZoomRect(slot, m);
                if (!zoom.IsEmpty)
                {
                    DrawZoomBox(canvas, paint, zoom, baseColor, pressed == FramePart.ZoomBox);
                    textRight = zoom.X - 4;
                }
            }

            var available = textRight - textX;
            if (available > 8)
            {
                using var font = new SKFont(Fonts.Sans, Theme.FontSize);
                var metrics = font.Metrics;
                var text = Fonts.Ellipsize(font, slotTitle, available);
                var baseline = MathF.Floor(
                    ((slot.Y + 2f + slot.Bottom - metrics.Ascent + metrics.Descent) / 2f) - metrics.Descent + 0.5f);
                paint.Color = Theme.TextColor(slotActive);
                paint.IsAntialias = true;
                canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, paint);
                paint.IsAntialias = false;
            }
        }
    }

    private void DrawBorders(SKCanvas canvas, SKPaint paint, ReadOnlySpan<SKColor> colors, bool floating)
    {
        var m = _metrics;
        var bw = m.BorderWidth;
        var top = m.TabHeight;
        var w = m.FrameWidth;
        var h = m.FrameHeight;
        var limit = floating ? 3 : 5;

        for (var i = 0; i < bw; i++)
        {
            var index = (int)(i / (float)bw * limit);
            var forward = floating ? Math.Min(index * 2, 5) : index;
            var reverse = floating
                ? (2 - index) == 2 ? 5 : (2 - index) * 2
                : (4 - index) == 4 ? 5 : 4 - index;

            paint.Color = colors[forward];
            canvas.DrawRect(i, top + i, w - (2 * i), 1, paint);
            canvas.DrawRect(i, top + i, 1, h - top - (2 * i), paint);

            paint.Color = colors[reverse];
            canvas.DrawRect(i, h - 1 - i, w - (2 * i), 1, paint);
            canvas.DrawRect(w - 1 - i, top + i, 1, h - top - (2 * i), paint);
        }

        var overdraw = (int)MathF.Ceiling(bw / 5f);
        paint.Color = colors[2];
        for (var i = 0; i < overdraw; i++)
        {
            canvas.DrawRect(m.TabRect.X + 2, top + i, m.TabRect.Width - 4, 1, paint);
        }
    }

    private void DrawTab(SKCanvas canvas, SKPaint paint, ReadOnlySpan<SKColor> border, string? title, bool active, FramePart? pressed)
    {
        var m = _metrics;
        var tab = m.TabRect;
        if (tab.IsEmpty)
        {
            return;
        }

        var baseColor = Theme.TabColor(active);
        var light = Theme.Tint(baseColor, Theme.LightenHalf);
        var bevel = Theme.Tint(baseColor, Theme.Lighten2);
        var shadow = Theme.Tint(baseColor, Theme.DarkenHalf);
        var frameLight = border[0];
        var frameDark = Theme.Tint(Theme.FrameColor(active), Theme.Darken3);

        paint.Color = frameLight;
        canvas.DrawRect(tab.X, tab.Y, 1, tab.Height, paint);
        canvas.DrawRect(tab.X, tab.Y, tab.Width, 1, paint);
        paint.Color = frameDark;
        canvas.DrawRect(tab.Right - 1, tab.Y, 1, tab.Height, paint);

        if (Theme.Flavor == LookFlavor.Haiku)
        {
            paint.Color = bevel;
            canvas.DrawRect(tab.X + 1, tab.Y + 1, 1, tab.Height - 1, paint);
            canvas.DrawRect(tab.X + 1, tab.Y + 1, tab.Width - 2, 1, paint);
            paint.Color = shadow;
            canvas.DrawRect(tab.Right - 2, tab.Y + 2, 1, tab.Height - 2, paint);

            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(tab.X, tab.Y),
                new SKPoint(tab.X, tab.Bottom),
                [light, baseColor],
                SKShaderTileMode.Clamp);
            paint.Shader = gradient;
            canvas.DrawRect(tab.X + 2, tab.Y + 2, tab.Width - 4, tab.Height - 2, paint);
            paint.Shader = null;
        }
        else
        {
            paint.Color = bevel;
            canvas.DrawRect(tab.X + 1, tab.Y + 1, 1, tab.Height - 1, paint);
            canvas.DrawRect(tab.X + 1, tab.Y + 1, tab.Width - 2, 1, paint);
            paint.Color = baseColor;
            canvas.DrawRect(tab.X + 2, tab.Y + 2, tab.Width - 4, tab.Height - 2, paint);
        }

        DrawTitle(canvas, paint, title, active);
        if (!m.CloseRect.IsEmpty)
        {
            DrawCloseBox(canvas, paint, m.CloseRect, baseColor, pressed == FramePart.CloseBox);
        }

        if (!m.ZoomRect.IsEmpty)
        {
            DrawZoomBox(canvas, paint, m.ZoomRect, baseColor, pressed == FramePart.ZoomBox);
        }
    }

    private void DrawTitle(SKCanvas canvas, SKPaint paint, string? title, bool active)
    {
        if (title is not { Length: > 0 })
        {
            return;
        }

        var m = _metrics;
        var tab = m.TabRect;
        using var font = new SKFont(Fonts.Sans, Theme.FontSize);
        var metrics = font.Metrics;
        var textX = m.CloseRect.IsEmpty ? tab.X + m.TextOffset : m.CloseRect.Right + m.TextOffset;
        var rightEdge = m.ZoomRect.IsEmpty ? tab.Right - m.TextOffset : m.ZoomRect.X;
        var available = rightEdge - textX - m.TextOffset + m.ButtonInset;
        if (available <= 0)
        {
            return;
        }

        var text = Fonts.Ellipsize(font, title, available);
        var baseline = MathF.Floor(
            ((tab.Y + 2f + tab.Bottom - metrics.Ascent + metrics.Descent) / 2f) - metrics.Descent + 0.5f);
        paint.Color = Theme.TextColor(active);
        paint.IsAntialias = true;
        canvas.DrawText(text, textX, baseline, SKTextAlign.Left, font, paint);
        paint.IsAntialias = false;
    }

    private static void DrawBlendedRect(SKCanvas canvas, SKPaint paint, Rect rect, SKColor baseColor, bool down)
    {
        var startColor = down
            ? Theme.Tint(baseColor, Theme.Darken1)
            : Theme.Tint(baseColor, Theme.LightenMax);
        var endColor = down ? Theme.Tint(baseColor, Theme.LightenHalf) : baseColor;

        if (Theme.Flavor == LookFlavor.Haiku)
        {
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(rect.X + 1, rect.Y + 1),
                new SKPoint(rect.Right - 1, rect.Bottom - 1),
                [startColor, endColor],
                SKShaderTileMode.Clamp);
            paint.Shader = gradient;
            canvas.DrawRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2, paint);
            paint.Shader = null;
        }
        else
        {
            paint.Color = down ? Theme.Tint(baseColor, Theme.Darken1) : baseColor;
            canvas.DrawRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2, paint);
        }

        paint.Color = Theme.Tint(baseColor, Theme.Darken2);
        canvas.DrawRect(rect.X, rect.Y, rect.Width, 1, paint);
        canvas.DrawRect(rect.X, rect.Bottom - 1, rect.Width, 1, paint);
        canvas.DrawRect(rect.X, rect.Y, 1, rect.Height, paint);
        canvas.DrawRect(rect.Right - 1, rect.Y, 1, rect.Height, paint);
    }

    private static void DrawCloseBox(SKCanvas canvas, SKPaint paint, Rect rect, SKColor baseColor, bool down) =>
        DrawBlendedRect(canvas, paint, rect, baseColor, down);

    private static void DrawZoomBox(SKCanvas canvas, SKPaint paint, Rect rect, SKColor baseColor, bool down)
    {
        var bigInset = (int)MathF.Floor(rect.Width / 4f);
        var big = new Rect(rect.X + bigInset, rect.Y + bigInset, rect.Width - bigInset, rect.Height - bigInset);
        DrawBlendedRect(canvas, paint, big, baseColor, down);

        var smallCut = (int)MathF.Floor(rect.Width / 2.1f);
        var small = new Rect(rect.X, rect.Y, rect.Width - smallCut, rect.Height - smallCut);
        DrawBlendedRect(canvas, paint, small, baseColor, down);
    }

    private void DrawResizeCorner(SKCanvas canvas, SKPaint paint, ReadOnlySpan<SKColor> colors)
    {
        var m = _metrics;
        var length = Theme.BorderResizeLength;
        var w = m.FrameWidth;
        var h = m.FrameHeight;
        var top = m.TabHeight;
        if (w <= length || h - top <= length)
        {
            return;
        }

        paint.Color = colors[0];
        canvas.DrawRect(w - m.BorderWidth, h - length, m.BorderWidth, 1, paint);
        canvas.DrawRect(w - length, h - m.BorderWidth, 1, m.BorderWidth, paint);
    }
}
