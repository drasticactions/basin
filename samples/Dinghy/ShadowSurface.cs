using System.Runtime.InteropServices;
using Basin.WindowManager;
using Wayland;

namespace Dinghy;

internal sealed class ShadowSurface : IDisposable
{
    private readonly WlSurface _surface;
    private readonly WmDecoration _decoration;
    private readonly ShmSlots _slots;
    private readonly OutputScales _scales;
    private readonly HashSet<WlOutput> _entered = [];

    private int _width;
    private int _height;
    private int _bufferWidth;
    private int _bufferHeight;
    private (int FrameWidth, int FrameHeight, int ShadowSize, uint Color, int Scale)? _lastKey;
    private bool _disposed;

    internal ShadowSurface(WmWindow window, WlCompositor compositor, WlShm shm, OutputScales scales)
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

    public void EnsureBuffer(int frameWidth, int frameHeight, int shadowSize, int scale)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return;
        }

        shadowSize = Math.Max(shadowSize, 0);
        scale = Math.Max(scale, 1);
        _width = frameWidth + (shadowSize * 2);
        _height = frameHeight + (shadowSize * 2);
        _bufferWidth = _width * scale;
        _bufferHeight = _height * scale;
        _surface.SetBufferScale(scale);
    }

    public void UpdateInputRegion(WlCompositor compositor)
    {
        var region = compositor.CreateRegion();
        _surface.SetInputRegion(region);
        region.Destroy();
    }

    public bool Render(int frameWidth, int frameHeight, int shadowSize, uint color, int scale)
    {
        if (_width <= 0 || _height <= 0 || _bufferWidth <= 0 || _bufferHeight <= 0
            || frameWidth <= 0 || frameHeight <= 0)
        {
            return false;
        }

        var key = (frameWidth, frameHeight, Math.Max(shadowSize, 0), color, Math.Max(scale, 1));
        if (_lastKey == key)
        {
            return false;
        }

        var stride = _bufferWidth * 4;
        var pixels = _slots.Prepare(_bufferWidth, _bufferHeight, stride);
        if (pixels == 0)
        {
            return false;
        }

        var bytes = _slots.CurrentBytes();
        bytes.Clear();
        ShadowNineSlice.Draw(
            bytes,
            _bufferWidth,
            frameWidth,
            frameHeight,
            Math.Max(shadowSize, 0),
            Math.Max(shadowSize, 0) / 2,
            color,
            Math.Max(scale, 1));

        _lastKey = key;
        return true;
    }

    public void SetOffset(int x, int y) => _decoration.SetOffset(x, y);

    public void Invalidate() => _lastKey = null;

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
}
