using Basin.Diagnostics;
using Wayland.Server.Shm;

namespace Basin;

internal sealed class ShmPool : IDisposable
{
    internal sealed class MappingHold
    {
        internal MappingHold(IMappedMemory mapping) => Mapping = mapping;

        internal IMappedMemory Mapping { get; }

        internal int Readers;

        internal bool Retired;

        private bool _freed;

        internal bool TryMarkFreed()
        {
            if (_freed)
            {
                return false;
            }

            _freed = true;
            return true;
        }
    }

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly List<MappingHold> _holds = new(1);
    private readonly Action<ShmPool>? _onFreed;
    private MappingHold _current;
    private int _refCount = 1;
    private bool _disposed;

    public ShmPool(IMappedMemory mapping, Action<ShmPool>? onFreed = null)
    {
        _current = new MappingHold(mapping);
        _holds.Add(_current);
        _onFreed = onFreed;
        BasinCounters.Track();
    }

    public int Size => _current.Mapping.Size;

    public bool IsWritable => _current.Mapping.IsWritable;

    public void Resize(int newSize)
    {
        _thread.Assert();
        if (_disposed || newSize <= Size)
        {
            return;
        }

        var next = new MappingHold(_current.Mapping.Remap(newSize));
        _holds.Add(next);
        RetireHold(_current);
        _current = next;
    }

    public bool TryGetRegion(int offset, int height, int stride, out nint address)
    {
        _thread.Assert();
        address = 0;
        if (_disposed || offset < 0 || stride <= 0 || height <= 0)
        {
            return false;
        }

        var end = (long)offset + (long)height * stride;
        if (end > _current.Mapping.Size)
        {
            return false;
        }

        address = _current.Mapping.Address + offset;
        return true;
    }

    public MappingHold AcquireReader(out nint baseAddress)
    {
        _thread.Assert();
        var hold = _current;
        hold.Readers++;
        baseAddress = hold.Mapping.Address;
        return hold;
    }

    public void ReleaseReader(MappingHold hold)
    {
        _thread.Assert();
        hold.Readers--;
        if (hold.Retired && hold.Readers == 0)
        {
            FreeHold(hold);
        }
    }

    public void AddRef()
    {
        _thread.Assert();
        _refCount++;
    }

    public void Release()
    {
        _thread.Assert();
        if (--_refCount > 0 || _disposed)
        {
            return;
        }

        Free();
    }

    public void Dispose() => Free();

    private void Free()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RetireHold(_current);
    }

    private void RetireHold(MappingHold hold)
    {
        hold.Retired = true;
        if (hold.Readers == 0)
        {
            FreeHold(hold);
        }
    }

    private void FreeHold(MappingHold hold)
    {
        if (!hold.TryMarkFreed())
        {
            return;
        }

        hold.Mapping.Dispose();
        _holds.Remove(hold);
        if (_disposed && _holds.Count == 0)
        {
            BasinCounters.Untrack();
            _onFreed?.Invoke(this);
        }
    }
}
