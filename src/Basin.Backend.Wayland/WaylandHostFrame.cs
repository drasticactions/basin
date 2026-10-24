using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Pixman;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// The chrome around a nested output's window, for a parent that does not draw decorations. It shows four bands cut from one buffer around the output.
/// The consumer draws every pixel and decides what each input means.
/// </summary>
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
    private HostFrameInsets _shadow;
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

    /// <summary>Gets the band widths that <see cref="SetInsets(HostFrameInsets, HostFrameInsets)"/> set last, shadow included, in logical pixels.</summary>
    public HostFrameInsets Insets => _insets;

    /// <summary>Gets the part of each inset that is a drop shadow. The window geometry and the input regions leave it out.</summary>
    public HostFrameInsets Shadow => _shadow;

    internal HostFrameInsets VisibleInsets => new(
        _insets.Top - _shadow.Top, _insets.Right - _shadow.Right, _insets.Bottom - _shadow.Bottom, _insets.Left - _shadow.Left);

    internal bool HasContent => _attached;

    /// <summary>Gets the width of the output plus the left and right insets, in logical pixels. It is 0 before the output has a mode.</summary>
    public int OuterWidth => _output.CurrentMode.Width == 0 ? 0 : _output.ContentLogicalWidth + _insets.Left + _insets.Right;

    /// <summary>Gets the height of the output plus the top and bottom insets, in logical pixels. It is 0 before the output has a mode.</summary>
    public int OuterHeight => _output.CurrentMode.Height == 0 ? 0 : _output.ContentLogicalHeight + _insets.Top + _insets.Bottom;

    /// <summary>Gets the output scale. A buffer for <see cref="Attach"/> has the outer size times this scale.</summary>
    public double Scale => _output.Scale;

    /// <summary>Gets a value indicating whether the parent window is active.</summary>
    public bool Activated { get; internal set; }

    /// <summary>Gets a value indicating whether the parent window is maximized.</summary>
    public bool Maximized { get; internal set; }

    /// <summary>Gets a value indicating whether the parent window is fullscreen.</summary>
    public bool Fullscreen { get; internal set; }

    /// <summary>Gets a value indicating whether an interactive resize of the parent window is in progress.</summary>
    public bool Resizing { get; internal set; }

    /// <summary>
    /// Occurs when <see cref="Activated"/>, <see cref="Maximized"/>, <see cref="Fullscreen"/>, <see cref="Resizing"/> or <see cref="Title"/> changes.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>Occurs when the pointer enters a band. The arguments are X and Y in logical pixels from the top-left of the outer box.</summary>
    public event Action<double, double>? PointerEnter;

    /// <summary>
    /// Occurs when the pointer moves over a band. The arguments are the time in milliseconds and X and Y in logical pixels from the top-left of the outer box.
    /// </summary>
    public event Action<uint, double, double>? PointerMotion;

    /// <summary>
    /// Occurs when a button changes state over a band. The arguments are the time in milliseconds, the Linux input event code and the pressed state.
    /// </summary>
    public event Action<uint, uint, bool>? PointerButton;

    /// <summary>Occurs when the pointer leaves the bands, or when the parent removes the pointer while it is on them.</summary>
    public event Action? PointerLeave;

    /// <summary>Sets the band widths with no shadow. Empty insets hide the bands.</summary>
    /// <param name="insets">The band widths in logical pixels.</param>
    /// <returns><see langword="false"/> when the frame is disposed or an inset is negative.</returns>
    public bool SetInsets(HostFrameInsets insets) => SetInsets(insets, default);

    /// <summary>
    /// Sets the band widths and the shadow part of each one, and configures the output again. Empty insets hide the bands.
    /// </summary>
    /// <param name="insets">The band widths in logical pixels, shadow included.</param>
    /// <param name="shadow">The shadow part of each band. Each side must be from 0 to the matching inset.</param>
    /// <returns><see langword="false"/> when the frame is disposed or a value is out of range.</returns>
    public bool SetInsets(HostFrameInsets insets, HostFrameInsets shadow)
    {
        if (_disposed)
        {
            return false;
        }

        if (insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0 || insets.Left < 0)
        {
            return false;
        }

        if (shadow.Top < 0 || shadow.Right < 0 || shadow.Bottom < 0 || shadow.Left < 0
            || shadow.Top > insets.Top || shadow.Right > insets.Right || shadow.Bottom > insets.Bottom || shadow.Left > insets.Left)
        {
            return false;
        }

        if (insets == _insets && shadow == _shadow)
        {
            return true;
        }

        _insets = insets;
        _shadow = shadow;
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

    /// <summary>
    /// Shows <paramref name="buffer"/> in the bands and commits them. A dmabuf the parent accepts is shared, and other buffers are copied into shm.
    /// </summary>
    /// <param name="buffer">The chrome image for the whole outer box, in physical pixels.</param>
    /// <param name="damage">The changed area in outer-box coordinates, or <see langword="null"/> for all of it.</param>
    /// <returns><see langword="false"/> when the frame is disposed, the insets are empty, the size does not match or the buffer cannot be imported.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Shows a named Xcursor theme cursor over the bands, at the frame's scale. An unknown name shows <c>left_ptr</c>.
    /// </summary>
    /// <param name="name">The Xcursor name, for example <c>top_left_corner</c>.</param>
    /// <returns><see langword="false"/> when the frame is disposed, the seat has no pointer or no cursor loads.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool ShowCursor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return !_disposed && _backend.ShowFrameCursor(name, Scale);
    }

    /// <summary>Raises the output's <see cref="WaylandOutput.CloseRequested"/> event.</summary>
    public void RequestClose() => _output.RequestClose();

    /// <summary>Gets the title of the output's parent window.</summary>
    public string Title => _output.Title;

    /// <summary>Starts an interactive move of the parent window. It needs a pointer serial from the parent, and it does nothing without one.</summary>
    public void StartMove()
    {
        if (!_disposed && _backend.ParentSeat is { } seat && _backend.LastPointerSerial is { } serial)
        {
            _output.ParentToplevel.Move(seat, serial);
            _backend.Flush();
        }
    }

    /// <summary>Starts an interactive resize of the parent window. It needs a pointer serial from the parent, and it does nothing without one.</summary>
    /// <param name="edges">The edges to move. <see cref="HostFrameEdges.None"/> does nothing.</param>
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

    /// <summary>Asks the parent to maximize or restore the window. The parent decides, and <see cref="StateChanged"/> reports the result.</summary>
    /// <param name="maximized"><see langword="true"/> to maximize, <see langword="false"/> to restore.</param>
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

    /// <summary>Asks the parent to minimize the window.</summary>
    public void SetMinimized()
    {
        if (!_disposed)
        {
            _output.ParentToplevel.SetMinimized();
            _backend.Flush();
        }
    }

    /// <summary>Releases the imported buffers and the shm slots and destroys the band surfaces. A second call does nothing.</summary>
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
            SetBandInput(i);
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
        SetBandInput(index);
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

    private void SetBandInput(int index)
    {
        if (_shadow.IsEmpty)
        {
            _surfaces[index].SetInputRegion(null);
            return;
        }

        var band = _bands[index];
        var contentW = OuterWidth - _insets.Left - _insets.Right;
        var contentH = OuterHeight - _insets.Top - _insets.Bottom;
        var left = Math.Max(band.X, _shadow.Left - _insets.Left);
        var top = Math.Max(band.Y, _shadow.Top - _insets.Top);
        var right = Math.Min(band.X + band.Width, contentW + _insets.Right - _shadow.Right);
        var bottom = Math.Min(band.Y + band.Height, contentH + _insets.Bottom - _shadow.Bottom);
        var region = _backend.ParentCompositor.CreateRegion();
        if (right > left && bottom > top)
        {
            region.Add(left - band.X, top - band.Y, right - left, bottom - top);
        }

        _surfaces[index].SetInputRegion(region);
        region.Dispose();
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
            throw new InvalidOperationException("The shared memory for the host frame was not created.");
        }

        var map = mmap(null, (nuint)_mappingSize, ProtReadWrite, MapShared, _mappingFd, 0);
        if ((nint)map == -1)
        {
            throw new InvalidOperationException("The shared memory for the host frame was not mapped.");
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
