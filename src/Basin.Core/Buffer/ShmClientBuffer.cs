using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class ShmClientBuffer : BufferBase
{
    private const uint WlBufferReleaseOpcode = 0;

    private readonly nint _resourceHandle;
    private readonly DrmFormat _format;
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

    public nint ResourceHandle => _resourceAlive ? _resourceHandle : 0;

    public static ShmClientBuffer? FromResource(nint bufferResourceHandle)
    {
        var shm = LibWaylandShm.FromResource(bufferResourceHandle);
        return shm is { } value ? new ShmClientBuffer(bufferResourceHandle, value) : null;
    }

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
