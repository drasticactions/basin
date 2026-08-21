using System.Runtime.InteropServices;
using Wayland;

namespace Basin.WindowManager;

public sealed unsafe class ShmSlots(WlShm shm) : IDisposable
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;
    private const uint MfdCloexec = 1;

    private readonly Slot[] _slots = [new Slot(), new Slot()];

    private int _fd = -1;
    private byte* _map;
    private nuint _mapSize;
    private WlShmPool? _pool;
    private int _slotCapacity;
    private int _lastPrepared = 1;
    private int _current = -1;

    public bool IsReady => _current >= 0 && _slots[_current].Buffer is not null;

    public WlBuffer? CurrentBuffer => _current >= 0 ? _slots[_current].Buffer : null;

    public nint Prepare(int width, int height, int stride)
    {
        var size = stride * height;
        if (size <= 0)
        {
            return 0;
        }

        int target;
        if (_pool is null || size > _slotCapacity)
        {
            if (!AllocateFresh(size))
            {
                return 0;
            }

            target = 0;
        }
        else
        {
            target = 1 - _lastPrepared;
            if (!_slots[target].Flag.Released)
            {
                target = _lastPrepared;
            }

            if (!_slots[target].Flag.Released)
            {
                if (!AllocateFresh(size))
                {
                    return 0;
                }

                target = 0;
            }
        }

        var slot = _slots[target];
        if (slot.Buffer is null || slot.Width != width || slot.Height != height || slot.Stride != stride)
        {
            if (slot.Buffer is { IsDestroyed: false } old)
            {
                old.Destroy();
            }

            var flag = new ReleaseFlag();
            var buffer = _pool!.CreateBuffer(_slotCapacity * target, width, height, stride, WlShm.Format.Argb8888);
            buffer.Release += (_, _) => flag.Released = true;
            slot.Buffer = buffer;
            slot.Flag = flag;
            slot.Width = width;
            slot.Height = height;
            slot.Stride = stride;
        }

        _lastPrepared = target;
        _current = target;
        return (nint)(_map + ((long)_slotCapacity * target));
    }

    public Span<byte> CurrentBytes()
    {
        if (_current < 0)
        {
            return [];
        }

        var slot = _slots[_current];
        return new Span<byte>(_map + ((long)_slotCapacity * _current), slot.Stride * slot.Height);
    }

    public void MarkAttached()
    {
        if (_current >= 0)
        {
            _slots[_current].Flag.Released = false;
        }
    }

    public void Dispose() => Release();

    private bool AllocateFresh(int size)
    {
        Release();

        var slotCapacity = size + (size / 2);
        var capacity = slotCapacity * 2;
        var fd = memfd_create("basin-wm-shm", MfdCloexec);
        if (fd < 0 || ftruncate(fd, capacity) != 0)
        {
            if (fd >= 0)
            {
                _ = close(fd);
            }

            return false;
        }

        var map = mmap(null, (nuint)capacity, ProtReadWrite, MapShared, fd, 0);
        if ((nint)map == -1)
        {
            _ = close(fd);
            return false;
        }

        _fd = fd;
        _map = (byte*)map;
        _mapSize = (nuint)capacity;
        _slotCapacity = slotCapacity;

        new Span<byte>(_map, capacity).Clear();

        _pool = shm.CreatePool(fd, capacity);
        _lastPrepared = 1;
        _current = -1;
        return true;
    }

    private void Release()
    {
        foreach (var slot in _slots)
        {
            if (slot.Buffer is { IsDestroyed: false } buffer)
            {
                buffer.Destroy();
            }

            slot.Buffer = null;
            slot.Flag = new ReleaseFlag();
            slot.Width = 0;
            slot.Height = 0;
            slot.Stride = 0;
        }

        if (_pool is { IsDestroyed: false } pool)
        {
            pool.Destroy();
        }

        _pool = null;
        if (_map is not null)
        {
            _ = munmap(_map, _mapSize);
            _map = null;
            _mapSize = 0;
        }

        if (_fd >= 0)
        {
            _ = close(_fd);
            _fd = -1;
        }

        _slotCapacity = 0;
        _current = -1;
    }

    private sealed class ReleaseFlag
    {
        public bool Released = true;
    }

    private sealed class Slot
    {
        public WlBuffer? Buffer;
        public ReleaseFlag Flag = new();
        public int Width;
        public int Height;
        public int Stride;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);
}
