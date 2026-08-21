using Basin.WindowManager;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal sealed class FrameSurface : IDisposable
{
    private readonly WlSurface _surface;
    private readonly WmDecoration _decoration;
    private readonly ShmSlots _slots;
    private readonly OutputScales _scales;
    private readonly HashSet<WlOutput> _entered = [];

    private int _scale = 1;
    private bool _titled = true;
    private int _contentWidth;
    private int _contentHeight;
    private int _width;
    private int _height;
    private bool _dirty = true;
    private bool _disposed;

    private string? _lastTitle;
    private string? _elided;
    private bool _lastActive;

    internal FrameSurface(WmWindow window, WlCompositor compositor, WlShm shm, OutputScales scales)
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
        _decoration = window.CreateDecorationBelow(_surface);
        _slots = new ShmSlots(shm);
    }

    public bool Mapped { get; private set; }

    public uint SurfaceId => _surface.Id;

    public int FrameWidth => _width;

    public int FrameHeight => _height;

    public bool Titled => _titled;

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

    public static Rect SystemBoxRect() => new(
        Theme.BorderWidth,
        Theme.BorderWidth,
        Theme.SystemBoxWidth,
        Theme.TitlebarHeight);

    public FramePart PartAt(int x, int y)
    {
        var bw = Theme.BorderWidth;
        var (_, top, _) = Theme.InsetsFor(_titled);
        if (x >= bw && y >= top && x < _width - bw && y < _height - bw)
        {
            return FramePart.Content;
        }

        if (_titled && SystemBoxRect().Contains(new Point(x, y)))
        {
            return FramePart.SystemBox;
        }

        if (_titled && y >= bw && y < top - bw && x >= bw && x < _width - bw)
        {
            return FramePart.Title;
        }

        return FramePart.Border;
    }

    public void EnsureGeometry(int contentWidth, int contentHeight, int scale, bool titled)
    {
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        scale = Math.Max(scale, 1);
        var (horizontal, top, bottom) = Theme.InsetsFor(titled);
        var width = contentWidth + (horizontal * 2);
        var height = contentHeight + top + bottom;
        if (_width != width || _height != height || _scale != scale || _titled != titled)
        {
            _width = width;
            _height = height;
            _scale = scale;
            _titled = titled;
            _elided = null;
            _dirty = true;
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

        var bw = Theme.BorderWidth;
        var (_, top, _) = Theme.InsetsFor(_titled);
        var region = compositor.CreateRegion();
        region.Add(0, 0, _width, _height);
        region.Subtract(bw, top, _contentWidth, _contentHeight);
        _surface.SetInputRegion(region);
        region.Destroy();
    }

    public bool Render(string? title, bool active)
    {
        if (_lastTitle != title || _lastActive != active)
        {
            _lastTitle = title;
            _lastActive = active;
            _elided = null;
            _dirty = true;
        }

        if (!_dirty || _width <= 0 || _height <= 0)
        {
            return false;
        }

        var bufferWidth = _width * _scale;
        var bufferHeight = _height * _scale;
        var stride = bufferWidth * 4;
        var pixels = _slots.Prepare(bufferWidth, bufferHeight, stride);
        if (pixels == 0)
        {
            return false;
        }

        _slots.CurrentBytes().Clear();
        var info = new SKImageInfo(bufferWidth, bufferHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, pixels, stride);
        if (surface is null)
        {
            return false;
        }

        Draw(surface.Canvas, title, active);
        surface.Canvas.Flush();
        _dirty = false;
        return true;
    }

    public void SetOffset(int swallowTop)
    {
        var (horizontal, top, _) = Theme.InsetsFor(_titled);
        _decoration.SetOffset(-horizontal, -top + swallowTop);
    }

    public void Invalidate()
    {
        _elided = null;
        _dirty = true;
    }

    public void SyncNextCommit() => _decoration.SyncNextCommit();

    public void Commit()
    {
        if (_slots.CurrentBuffer is not { } buffer)
        {
            return;
        }

        _surface.Attach(buffer, 0, 0);
        _surface.DamageBuffer(0, 0, _width * _scale, _height * _scale);
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
        _elided = null;
        _lastActive = false;
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

    private void Draw(SKCanvas canvas, string? title, bool active)
    {
        var scale = _scale;
        var bw = Theme.BorderWidth * scale;
        var width = _width * scale;
        var height = _height * scale;
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        Fill(canvas, paint, 0, 0, width, bw, Theme.WindowLine);
        Fill(canvas, paint, 0, height - bw, width, bw, Theme.WindowLine);
        Fill(canvas, paint, 0, bw, bw, height - (2 * bw), Theme.WindowLine);
        Fill(canvas, paint, width - bw, bw, bw, height - (2 * bw), Theme.WindowLine);

        if (!_titled)
        {
            return;
        }

        var line = scale;
        var barX = bw;
        var barY = bw;
        var barWidth = width - (2 * bw);
        var barHeight = Theme.TitlebarHeight * scale;
        Fill(canvas, paint, barX, barY, barWidth, barHeight, active ? Theme.TitleActiveBg : Theme.TitleInactiveBg);
        Fill(canvas, paint, barX, barY + barHeight, barWidth, line, Theme.WindowLine);

        var boxWidth = Theme.SystemBoxWidth * scale;
        Fill(canvas, paint, barX, barY, boxWidth, barHeight, Theme.ChromeBg);
        Fill(canvas, paint, barX + boxWidth, barY, line, barHeight, Theme.WindowLine);

        var thickness = Math.Max(barHeight / 14, 1);
        var glyphWidth = boxWidth * 5 / 8;
        var glyphX = barX + ((boxWidth - glyphWidth) / 2);
        var glyphY = barY + ((barHeight - (5 * thickness)) / 2);
        Fill(canvas, paint, glyphX, glyphY, glyphWidth, thickness, Theme.WindowLine);
        Fill(canvas, paint, glyphX, glyphY + (2 * thickness), glyphWidth, thickness, Theme.WindowLine);
        Fill(canvas, paint, glyphX, glyphY + (4 * thickness), glyphWidth, thickness, Theme.WindowLine);

        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        using var font = new SKFont(Fonts.Sans, Theme.FontSize * scale);
        font.Subpixel = true;
        var pad = 6 * scale;
        var available = barWidth - boxWidth - line - (2 * pad);
        if (available <= 0)
        {
            return;
        }

        _elided ??= Fonts.Ellipsize(font, title, available);
        var textWidth = font.MeasureText(_elided);
        var textX = barX + boxWidth + line + pad + Math.Max((available - textWidth) / 2f, 0f);
        var metrics = font.Metrics;
        var baseline = barY + ((barHeight - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        textPaint.Color = Theme.Color(active ? Theme.TitleActiveText : Theme.TitleInactiveText);
        canvas.DrawText(_elided, textX, baseline, SKTextAlign.Left, font, textPaint);
    }

    private static void Fill(SKCanvas canvas, SKPaint paint, int x, int y, int width, int height, uint color)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        paint.Color = Theme.Color(color);
        canvas.DrawRect(x, y, width, height, paint);
    }
}
