using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Pixman;
using Wayland.Server.Shm;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

internal sealed class ManagedShmBuffer : BufferBase
{
    private readonly ShmPool _pool;
    private readonly ShmPool.MappingHold _hold;
    private readonly int _offset;
    private readonly int _stride;
    private readonly DrmFormat _format;

    public override DrmFormat Format => _format;
    private readonly bool _writable;
    private readonly ISharedMemory? _guard;
    private PixmanRegion32? _dirty;
    private nint _shadow;

    internal ManagedShmBuffer(ShmPool pool, int offset, int width, int height, int stride, DrmFormat format, ISharedMemory? guard = null)
        : base(width, height)
    {
        _pool = pool;
        pool.AddRef();
        _hold = pool.AcquireReader(out _);
        _offset = offset;
        _stride = stride;
        _format = format;
        _writable = pool.IsWritable;
        _guard = guard;
    }

    private nint Address => _hold.Mapping.Address + _offset;

    internal bool IsGuarded => _guard is not null;

    internal void AccumulateDirty(PixmanRegion32 damage)
    {
        _dirty ??= new PixmanRegion32();
        _dirty.UnionWith(damage);
    }

    internal void MarkAllDirty()
    {
        _dirty ??= new PixmanRegion32();
        _dirty.Reset(new PixmanBox32(0, 0, Width, Height));
    }

    internal void SyncShadow()
    {
        if (_guard is null || _dirty is null || _dirty.IsEmpty || IsStorageFreed)
        {
            return;
        }

        if (_shadow == 0)
        {
            unsafe
            {
                _shadow = (nint)NativeMemory.AllocZeroed((nuint)((long)_stride * Height));
            }
        }

        var bpp = _format.BytesPerPixel();
        foreach (var rect in RegionRects.Of(_dirty))
        {
            var x1 = Math.Max(rect.X1, 0);
            var y1 = Math.Max(rect.Y1, 0);
            var x2 = Math.Min(rect.X2, Width);
            var y2 = Math.Min(rect.Y2, Height);
            if (x2 <= x1 || y2 <= y1)
            {
                continue;
            }

            var rowStart = (long)y1 * _stride + (long)x1 * bpp;
            if (!_guard.TryCopyRows(
                    _shadow + (nint)rowStart, _stride,
                    Address + (nint)rowStart, _stride,
                    (x2 - x1) * bpp, y2 - y1))
            {
                Log.Warn($"wl_shm: guarded copy faulted (pool truncated?); keeping the previous frame");
                return;
            }
        }

        _dirty.Clear();
    }

    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if ((access & BufferDataAccess.Write) != 0)
        {
            if (!_writable)
            {
                view = default;
                return false;
            }

            view = new BufferDataView(Address, _stride, _format);
            return true;
        }

        if (_guard is not null && _shadow != 0)
        {
            view = new BufferDataView(_shadow, _stride, _format);
            return true;
        }

        view = new BufferDataView(Address, _stride, _format);
        return true;
    }

    protected override void OnFreeStorage()
    {
        if (_shadow != 0)
        {
            unsafe
            {
                NativeMemory.Free((void*)_shadow);
            }

            _shadow = 0;
        }

        _dirty?.Dispose();
        _dirty = null;
        _pool.ReleaseReader(_hold);
        _pool.Release();
    }
}
