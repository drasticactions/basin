using Basin.Diagnostics;

namespace Basin;

public abstract class BufferBase : IBuffer
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private int _lockCount;
    private int _accessDepth;
    private bool _destroyed;
    private bool _storageFreed;

    protected BufferBase(int width, int height)
    {
        Width = width;
        Height = height;
        BasinCounters.Track();
    }

    public int Width { get; }

    public int Height { get; }

    public int LockCount => _lockCount;

    public virtual DrmFormat Format => DrmFormat.Invalid;

    public bool IsDestroyed => _destroyed;

    protected bool IsStorageFreed => _storageFreed;

    private ActionSet _released;
    private ActionSet _destroyedHandlers;

    public event Action? Released
    {
        add => _released.Add(value);
        remove => _released.Remove(value);
    }

    public event Action? Destroyed
    {
        add => _destroyedHandlers.Add(value);
        remove => _destroyedHandlers.Remove(value);
    }

    public BufferLock Lock()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_storageFreed, this);
        _lockCount++;
        return new BufferLock(this);
    }

    public void Unlock()
    {
        _thread.Assert();
        if (_lockCount <= 0)
        {
            throw new InvalidOperationException("Unlock without a matching Lock.");
        }

        _lockCount--;
        if (_lockCount == 0)
        {
            _released.Raise();
            if (_destroyed)
            {
                FreeStorage();
            }
        }
    }

    public void Destroy()
    {
        _thread.Assert();
        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        _destroyedHandlers.Raise();
        if (_lockCount == 0)
        {
            FreeStorage();
        }
    }

    public bool BeginDataAccess(BufferDataAccess access, out BufferDataView view)
    {
        _thread.Assert();
        if (_storageFreed || !TryMap(access, out view))
        {
            view = default;
            return false;
        }

        _accessDepth++;
        return true;
    }

    public void EndDataAccess()
    {
        _thread.Assert();
        if (_accessDepth <= 0)
        {
            throw new InvalidOperationException("EndDataAccess without a matching BeginDataAccess.");
        }

        _accessDepth--;
        Unmap();
    }

    protected abstract bool TryMap(BufferDataAccess access, out BufferDataView view);

    protected virtual void Unmap()
    {
    }

    protected abstract void OnFreeStorage();

    private void FreeStorage()
    {
        if (_storageFreed)
        {
            return;
        }

        _storageFreed = true;
        OnFreeStorage();
        BasinCounters.Untrack();
    }
}
