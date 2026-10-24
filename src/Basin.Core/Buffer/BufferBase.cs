using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// A base for <see cref="IBuffer"/> implementations. It counts locks, frees the storage once the buffer is destroyed and unlocked, and brackets CPU access.
/// </summary>
public abstract class BufferBase : IBuffer
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private int _lockCount;
    private int _accessDepth;
    private bool _destroyed;
    private bool _storageFreed;

    /// <summary>Initializes a new instance of the <see cref="BufferBase"/> class.</summary>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    protected BufferBase(int width, int height)
    {
        Width = width;
        Height = height;
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <inheritdoc/>
    public int LockCount => _lockCount;

    /// <inheritdoc/>
    public virtual DrmFormat Format => DrmFormat.Invalid;

    /// <inheritdoc/>
    public virtual bool TryGetDmabuf(out DmabufAttributes attributes)
    {
        attributes = default;
        return false;
    }

    /// <inheritdoc/>
    public bool IsDestroyed => _destroyed;

    /// <summary>
    /// Gets a value indicating whether <see cref="OnFreeStorage"/> already ran. Nothing can map or lock the buffer after that.
    /// </summary>
    protected bool IsStorageFreed => _storageFreed;

    private ActionSet _released;
    private ActionSet _destroyedHandlers;

    /// <inheritdoc/>
    public event Action? Released
    {
        add => _released.Add(value);
        remove => _released.Remove(value);
    }

    /// <inheritdoc/>
    public event Action? Destroyed
    {
        add => _destroyedHandlers.Add(value);
        remove => _destroyedHandlers.Remove(value);
    }

    /// <summary>
    /// Adds one lock. The storage stays valid until each lock is released.
    /// </summary>
    /// <returns>A lock that calls <see cref="Unlock"/> when it is disposed.</returns>
    /// <exception cref="ObjectDisposedException">The storage is already freed.</exception>
    public BufferLock Lock()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_storageFreed, this);
        _lockCount++;
        return new BufferLock(this);
    }

    /// <summary>
    /// Removes one lock. At zero it raises <see cref="Released"/>, and it frees the storage when the buffer is already destroyed.
    /// </summary>
    /// <exception cref="InvalidOperationException">No lock is held.</exception>
    public void Unlock()
    {
        _thread.Assert();
        if (_lockCount <= 0)
        {
            throw new InvalidOperationException("Unlock was called without a matching Lock.");
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

    /// <summary>
    /// Marks the buffer destroyed and raises <see cref="Destroyed"/>. The storage is freed now when no lock is held, or at the last <see cref="Unlock"/>. A second call does nothing.
    /// </summary>
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

    /// <summary>
    /// Maps the pixels through <see cref="TryMap"/>. Each successful call must be paired with one <see cref="EndDataAccess"/>.
    /// </summary>
    /// <param name="access">The kinds of access that the caller needs.</param>
    /// <param name="view">The mapped pixels, or the default value when the call fails.</param>
    /// <returns><see langword="true"/> when the pixels are mapped. It is <see langword="false"/> after the storage is freed or when <see cref="TryMap"/> refuses.</returns>
    public bool BeginDataAccess(BufferDataAccess access, out BufferDataView view)
    {
        _thread.Assert();
        if (_storageFreed || !TryMap(access, out view))
        {
            view = default;
            return false;
        }

        _accessDepth++;
        if ((access & BufferDataAccess.Write) != 0)
        {
            WriteCount++;
        }

        return true;
    }

    /// <inheritdoc/>
    public ulong WriteCount { get; private set; }

    /// <summary>
    /// Ends one access and calls <see cref="Unmap"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No access is open.</exception>
    public void EndDataAccess()
    {
        _thread.Assert();
        if (_accessDepth <= 0)
        {
            throw new InvalidOperationException("EndDataAccess was called without a matching BeginDataAccess.");
        }

        _accessDepth--;
        Unmap();
    }

    /// <summary>
    /// Maps the storage for CPU access. <see cref="BeginDataAccess"/> calls it only while the storage is live.
    /// </summary>
    /// <param name="access">The kinds of access that the caller needs.</param>
    /// <param name="view">The mapped pixels when the call succeeds.</param>
    /// <returns><see langword="true"/> when the storage is mapped for <paramref name="access"/>.</returns>
    protected abstract bool TryMap(BufferDataAccess access, out BufferDataView view);

    /// <summary>
    /// Ends one mapping that <see cref="TryMap"/> started. The base implementation does nothing.
    /// </summary>
    protected virtual void Unmap()
    {
    }

    /// <summary>
    /// Frees the storage. It runs once, when the buffer is destroyed and no lock is held.
    /// </summary>
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
