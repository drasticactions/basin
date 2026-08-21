using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Pixman;
using Wayland;

namespace Basin.Backend.Wayland;

public sealed class WaylandHostFrame : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    private enum Band
    {
        Top,
        Left,
        Right,
        Bottom,
    }

    private const int BandCount = 4;

    private readonly WaylandBackend _backend;
    private readonly WaylandOutput _output;
    private readonly WlSurface[] _surfaces = new WlSurface[BandCount];
    private readonly WlSubsurface[] _subsurfaces = new WlSubsurface[BandCount];
    private readonly WpViewport[] _viewports = new WpViewport[BandCount];
    private readonly Box[] _bands = new Box[BandCount];
    private readonly bool[] _bandShown = new bool[BandCount];
    private readonly Dictionary<IBuffer, ChromeBuffer> _imported = [];

    private readonly ShmSlot?[] _slots = new ShmSlot?[2];
    private nint _mapping;
    private int _mappingSize;
    private int _mappingFd = -1;
    private WlShmPool? _pool;
    private int _slotWidth;
    private int _slotHeight;

    private HostFrameInsets _insets;
    private bool _disposed;
    private bool _attached;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    internal WaylandHostFrame(WaylandBackend backend, WaylandOutput output, WlSurface parent, WlSubcompositor subcompositor)
    {
        _backend = backend;
        _output = output;
        for (var i = 0; i < BandCount; i++)
        {
            var surface = backend.ParentCompositor.CreateSurface();
            var subsurface = subcompositor.GetSubsurface(surface, parent);

            subsurface.SetDesync();

            subsurface.PlaceBelow(parent);
            _surfaces[i] = surface;
            _subsurfaces[i] = subsurface;
            _viewports[i] = backend.ParentViewporter!.GetViewport(surface);
        }
    }

    public HostFrameInsets Insets => _insets;

    internal bool HasContent => _attached;

    public int OuterWidth => _output.CurrentMode.Width == 0 ? 0 : _output.ContentLogicalWidth + _insets.Left + _insets.Right;

    public int OuterHeight => _output.CurrentMode.Height == 0 ? 0 : _output.ContentLogicalHeight + _insets.Top + _insets.Bottom;

    public double Scale => _output.Scale;

    public bool Activated { get; internal set; }

    public bool Maximized { get; internal set; }

    public bool Fullscreen { get; internal set; }

    public bool Resizing { get; internal set; }

    public event Action? StateChanged;

    public event Action<double, double>? PointerEnter;

    public event Action<uint, double, double>? PointerMotion;

    public event Action<uint, uint, bool>? PointerButton;

    public event Action? PointerLeave;

    public bool SetInsets(HostFrameInsets insets)
    {
        if (_disposed)
        {
            return false;
        }

        if (insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0 || insets.Left < 0)
        {
            return false;
        }

        if (insets == _insets)
        {
            return true;
        }

        _insets = insets;
        if (insets.IsEmpty)
        {
            HideBands();
        }

        _output.ApplyHostFrameInsets();
        return true;
    }

    private void HideBands()
    {
        for (var i = 0; i < BandCount; i++)
        {
            if (!_bandShown[i])
            {
                continue;
            }

            _bandShown[i] = false;
            _surfaces[i].Attach(null, 0, 0);
            _surfaces[i].Commit();
        }

        _attached = false;
        _backend.Flush();
    }

    public bool Attach(IBuffer buffer, PixmanRegion32? damage = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (_disposed || _insets.IsEmpty)
        {
            return false;
        }

        var scale = Scale;
        var outerW = OuterWidth;
        var outerH = OuterHeight;

        var physical = OutputScaling.ToPhysical(new Box(0, 0, outerW, outerH), scale);
        var physicalW = physical.Width;
        var physicalH = physical.Height;
        if (physicalW <= 0 || physicalH <= 0 || buffer.Width != physicalW || buffer.Height != physicalH)
        {
            return false;
        }

        var proxy = TryImportDmabuf(buffer);
        if (proxy is null)
        {
            if (!TryCopyIntoSlot(buffer, physicalW, physicalH, out proxy))
            {
                return false;
            }
        }

        LayOutBands(outerW, outerH);
        for (var i = 0; i < BandCount; i++)
        {
            CommitBand((Band)i, proxy, scale, damage);
        }

        _attached = true;

        _output.ApplyParentState();
        _backend.Flush();
        return true;
    }

    public void StartMove()
    {
        if (!_disposed && _backend.ParentSeat is { } seat && _backend.LastPointerSerial is { } serial)
        {
            _output.ParentToplevel.Move(seat, serial);
            _backend.Flush();
        }
    }

    public void StartResize(HostFrameEdges edges)
    {
        if (edges == HostFrameEdges.None)
        {
            return;
        }

        if (!_disposed && _backend.ParentSeat is { } seat && _backend.LastPointerSerial is { } serial)
        {
            _output.ParentToplevel.Resize(seat, serial, (XdgToplevel.ResizeEdge)edges);
            _backend.Flush();
        }
    }

    public void SetMaximized(bool maximized)
    {
        if (_disposed)
        {
            return;
        }

        if (maximized)
        {
            _output.ParentToplevel.SetMaximized();
        }
        else
        {
            _output.ParentToplevel.UnsetMaximized();
        }

        _backend.Flush();
    }

    public void SetMinimized()
    {
        if (!_disposed)
        {
            _output.ParentToplevel.SetMinimized();
            _backend.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var (buffer, chrome) in _imported)
        {
            if (chrome.Presented)
            {
                chrome.Presented = false;
                buffer.Unlock();
            }

            if (!chrome.Proxy.IsDestroyed)
            {
                chrome.Proxy.Dispose();
            }
        }

        _imported.Clear();
        DestroySlots();
        for (var i = 0; i < BandCount; i++)
        {
            _viewports[i].Dispose();
            _subsurfaces[i].Dispose();
            _surfaces[i].Dispose();
        }

        _backend.Flush();
    }

    internal void RaiseStateChanged() => StateChanged?.Invoke();

    internal bool TryLocate(WlSurface? surface, out Point origin)
    {
        for (var i = 0; i < BandCount; i++)
        {
            if (_surfaces[i] == surface)
            {
                origin = new Point(_bands[i].X + _insets.Left, _bands[i].Y + _insets.Top);
                return true;
            }
        }

        origin = default;
        return false;
    }

    internal void OnPointerEnter(double x, double y) => PointerEnter?.Invoke(x, y);

    internal void OnPointerMotion(uint timeMs, double x, double y) => PointerMotion?.Invoke(timeMs, x, y);

    internal void OnPointerButton(uint timeMs, uint button, bool pressed) => PointerButton?.Invoke(timeMs, button, pressed);

    internal void OnPointerLeave() => PointerLeave?.Invoke();

    internal void OnOutputResized()
    {
        if (_disposed || _insets.IsEmpty || !_attached)
        {
            return;
        }

        LayOutBands(OuterWidth, OuterHeight);
        for (var i = 0; i < BandCount; i++)
        {
            var band = (Band)i;
            if (!_bandShown[i])
            {
                continue;
            }

            _subsurfaces[i].SetPosition(_bands[i].X, _bands[i].Y);
            _viewports[i].SetDestination(_bands[i].Width, _bands[i].Height);
            _surfaces[i].Commit();
        }
    }

    private void LayOutBands(int outerW, int outerH)
    {
        var contentW = outerW - _insets.Left - _insets.Right;
        var contentH = outerH - _insets.Top - _insets.Bottom;
        _bands[(int)Band.Top] = new Box(-_insets.Left, -_insets.Top, outerW, _insets.Top);
        _bands[(int)Band.Left] = new Box(-_insets.Left, 0, _insets.Left, contentH);
        _bands[(int)Band.Right] = new Box(contentW, 0, _insets.Right, contentH);
        _bands[(int)Band.Bottom] = new Box(-_insets.Left, contentH, outerW, _insets.Bottom);
    }

    private void CommitBand(Band band, WlBuffer proxy, double scale, PixmanRegion32? damage)
    {
        var index = (int)band;
        var box = _bands[index];
        var surface = _surfaces[index];
        if (box.Width <= 0 || box.Height <= 0)
        {
            if (_bandShown[index])
            {
                _bandShown[index] = false;
                surface.Attach(null, 0, 0);
                surface.Commit();
            }

            return;
        }

        var sourceX = (box.X + _insets.Left) * scale;
        var sourceY = (box.Y + _insets.Top) * scale;
        _viewports[index].SetSource(
            WlFixed.FromDouble(sourceX),
            WlFixed.FromDouble(sourceY),
            WlFixed.FromDouble(box.Width * scale),
            WlFixed.FromDouble(box.Height * scale));
        _viewports[index].SetDestination(box.Width, box.Height);
        _subsurfaces[index].SetPosition(box.X, box.Y);
        surface.Attach(proxy, 0, 0);
        if (damage is null)
        {
            surface.Damage(0, 0, box.Width, box.Height);
        }
        else
        {
            AddBandDamage(surface, box, damage);
        }

        surface.Commit();
        _bandShown[index] = true;
    }

    private void AddBandDamage(WlSurface surface, in Box band, PixmanRegion32 damage)
    {
        var offsetX = band.X + _insets.Left;
        var offsetY = band.Y + _insets.Top;
        foreach (var rect in RegionRects.Of(damage))
        {
            var x1 = Math.Max(rect.X1 - offsetX, 0);
            var y1 = Math.Max(rect.Y1 - offsetY, 0);
            var x2 = Math.Min(rect.X2 - offsetX, band.Width);
            var y2 = Math.Min(rect.Y2 - offsetY, band.Height);
            if (x2 > x1 && y2 > y1)
            {
                surface.Damage(x1, y1, x2 - x1, y2 - y1);
            }
        }
    }

    private ChromeBuffer? ImportedOrNull(IBuffer buffer) => _imported.GetValueOrDefault(buffer);

    private WlBuffer? TryImportDmabuf(IBuffer buffer)
    {
        if (ImportedOrNull(buffer) is { } cached)
        {
            return Present(buffer, cached);
        }

        if (_backend.ParentDmabuf is not { } dmabuf ||
            !buffer.TryGetDmabuf(out var attributes) ||
            !_backend.ParentDmabufFormats.Contains(attributes.Format, attributes.Modifier))
        {
            return null;
        }

        var bufferParams = dmabuf.CreateParams();
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            bufferParams.Add(
                attributes.Fds[plane],
                (uint)plane,
                attributes.Offsets[plane],
                attributes.Strides[plane],
                (uint)(attributes.Modifier >> 32),
                (uint)attributes.Modifier);
        }

        var proxy = bufferParams.CreateImmed(buffer.Width, buffer.Height, (uint)attributes.Format, 0);
        bufferParams.Dispose();

        var chrome = new ChromeBuffer(proxy);

        proxy.Release += (_, _) =>
        {
            if (chrome.Presented)
            {
                chrome.Presented = false;
                buffer.Unlock();
            }
        };
        buffer.Destroyed += () =>
        {
            _imported.Remove(buffer);
            if (chrome.Presented)
            {
                chrome.Presented = false;
                buffer.Unlock();
            }

            if (!proxy.IsDestroyed)
            {
                proxy.Dispose();
                _backend.Flush();
            }
        };

        _imported[buffer] = chrome;
        return Present(buffer, chrome);
    }

    private static WlBuffer Present(IBuffer buffer, ChromeBuffer chrome)
    {
        if (!chrome.Presented)
        {
            buffer.Lock();
            chrome.Presented = true;
        }

        return chrome.Proxy;
    }

    private unsafe bool TryCopyIntoSlot(IBuffer source, int width, int height, [NotNullWhen(true)] out WlBuffer? proxy)
    {
        proxy = null;
        if (_pool is null || _slotWidth != width || _slotHeight != height)
        {
            RebuildSlots(width, height);
        }

        ShmSlot? slot = null;
        foreach (var candidate in _slots)
        {
            if (candidate is { Busy: false })
            {
                slot = candidate;
                break;
            }
        }

        if (slot is null || !source.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        try
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                System.Buffer.MemoryCopy(
                    (void*)(view.Data + y * view.Stride),
                    (void*)(slot.Data + y * rowBytes),
                    rowBytes,
                    rowBytes);
            }
        }
        finally
        {
            source.EndDataAccess();
        }

        slot.Busy = true;
        proxy = slot.Proxy;
        return true;
    }

    private unsafe void RebuildSlots(int width, int height)
    {
        DestroySlots();
        var stride = width * 4;
        _mappingSize = stride * height * 2;
        _mappingFd = memfd_create("basin-wl-hostframe", 1 );
        if (_mappingFd < 0 || ftruncate(_mappingFd, _mappingSize) != 0)
        {
            throw new InvalidOperationException("host frame shm creation failed");
        }

        var map = mmap(null, (nuint)_mappingSize, ProtReadWrite, MapShared, _mappingFd, 0);
        if ((nint)map == -1)
        {
            throw new InvalidOperationException("host frame shm mmap failed");
        }

        _mapping = (nint)map;
        _pool = _backend.ParentShm.CreatePool(_mappingFd, _mappingSize);
        _slotWidth = width;
        _slotHeight = height;
        for (var i = 0; i < 2; i++)
        {
            var offset = i * stride * height;

            var bufferProxy = _pool.CreateBuffer(offset, width, height, stride, WlShm.Format.Argb8888);
            var slot = new ShmSlot(bufferProxy, _mapping + offset);
            bufferProxy.Release += (_, _) => slot.Busy = false;
            _slots[i] = slot;
        }
    }

    private unsafe void DestroySlots()
    {
        foreach (var slot in _slots)
        {
            if (slot is not null && !slot.Proxy.IsDestroyed)
            {
                slot.Proxy.Dispose();
            }
        }

        Array.Clear(_slots);
        _pool?.Dispose();
        _pool = null;
        _slotWidth = 0;
        _slotHeight = 0;
        if (_mapping != 0)
        {
            munmap((void*)_mapping, (nuint)_mappingSize);
            _mapping = 0;
            close(_mappingFd);
            _mappingFd = -1;
        }
    }

    private sealed class ShmSlot(WlBuffer proxy, nint data)
    {
        public WlBuffer Proxy { get; } = proxy;

        public nint Data { get; } = data;

        public bool Busy { get; set; }
    }

    private sealed class ChromeBuffer(WlBuffer proxy)
    {
        public WlBuffer Proxy { get; } = proxy;

        public bool Presented { get; set; }
    }
}
