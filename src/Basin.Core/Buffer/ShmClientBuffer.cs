using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Wraps a libwayland <c>wl_shm</c> buffer and sends <c>wl_buffer.release</c> when the last lock goes. The pixels stay readable after the client destroys the resource while a lock holds them.
/// </summary>
public sealed class ShmClientBuffer : BufferBase
{
    private const uint WlBufferReleaseOpcode = 0;

    private readonly nint _resourceHandle;
    private readonly DrmFormat _format;

    /// <inheritdoc/>
    public override DrmFormat Format => _format;
    private WlForeignDestroyListener? _destroyListener;
    private WlShmBufferRef _shm;
    private bool _resourceAlive;
    private WlShmPoolRef _poolRef;
    private nint _staleData;
    private int _staleStride;

    private ShmClientBuffer(nint resourceHandle, WlShmBufferRef shm)
        : base(shm.Width, shm.Height)
    {
        _resourceHandle = resourceHandle;
        _shm = shm;
        _format = DrmFormatExtensions.FromWlShm(shm.Format);
        _resourceAlive = true;
        _destroyListener = WlForeignResource.AddDestroyListener(resourceHandle, OnResourceDestroyed);
        Released += SendRelease;
    }

    /// <summary>Gets the native <c>wl_buffer</c> handle, or zero after the client destroyed the resource.</summary>
    public nint ResourceHandle => _resourceAlive ? _resourceHandle : 0;

    /// <summary>
    /// Wraps a libwayland shm buffer resource.
    /// </summary>
    /// <param name="bufferResourceHandle">The native handle of the <c>wl_buffer</c> resource.</param>
    /// <returns>A new wrapper, or <see langword="null"/> when the resource is not a libwayland shm buffer.</returns>
    public static ShmClientBuffer? FromResource(nint bufferResourceHandle)
    {
        var shm = LibWaylandShm.FromResource(bufferResourceHandle);
        return shm is { } value ? new ShmClientBuffer(bufferResourceHandle, value) : null;
    }

    /// <inheritdoc/>
    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if (!_resourceAlive)
        {
            if (_staleData == 0)
            {
                view = default;
                return false;
            }

            view = new BufferDataView(_staleData, _staleStride, _format);
            return true;
        }

        if (_threadAccessDepth == 0)
        {
            _shm.BeginAccess();
            _bracketed = true;
        }

        _threadAccessDepth++;
        view = new BufferDataView(_shm.Data, _shm.Stride, _format);
        return true;
    }

    /// <inheritdoc/>
    protected override void Unmap()
    {
        if (_resourceAlive)
        {
            _threadAccessDepth--;
            if (_bracketed)
            {
                _shm.EndAccess();
                _bracketed = false;
            }
        }
    }

    [ThreadStatic]
    private static int _threadAccessDepth;

    private bool _bracketed;

    /// <inheritdoc/>
    protected override void OnFreeStorage()
    {
        _destroyListener?.Dispose();
        _destroyListener = null;
        _poolRef.Unref();
        _poolRef = default;
        _staleData = 0;
    }

    private void SendRelease()
    {
        if (_resourceAlive)
        {
            WlForeignResource.PostEvent(_resourceHandle, WlBufferReleaseOpcode, default);
        }
    }

    private void OnResourceDestroyed()
    {
        if (LockCount > 0 && !IsDestroyed)
        {
            var bracket = _threadAccessDepth == 0;
            if (bracket)
            {
                _shm.BeginAccess();
            }

            _staleData = _shm.Data;
            _staleStride = _shm.Stride;
            if (bracket)
            {
                _shm.EndAccess();
            }

            _poolRef = _shm.RefPool();
        }

        _resourceAlive = false;
        _destroyListener = null;
        Destroy();
    }
}
