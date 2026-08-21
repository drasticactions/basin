using Wayland;
using Wayland.Server;
using Wayland.Server.Shm;

namespace Basin;

public sealed class ManagedShmGlobal : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly ISharedMemory? _sharedMemory;
    private readonly ClientBufferRegistry _registry;
    private readonly ShmLimits _limits;
    private readonly DrmFormat[] _formats;
    private readonly Dictionary<WlClient, ClientState> _clients = [];

    private sealed class ClientState
    {
        public readonly HashSet<ShmPool> Pools = [];
        public ISharedMemory? SharedMemory;
        public int PoolCount;
        public long MappedBytes;
        public int BufferCount;
    }

    private sealed class PoolEntry
    {
        public required ShmPool Pool;
        public long AccountedBytes;
        public bool Released;
    }

    public ManagedShmGlobal(
        WlServerDisplay display,
        ISharedMemory? sharedMemory,
        ClientBufferRegistry buffers,
        ShmAccessPolicy policy = ShmAccessPolicy.Direct,
        ShmLimits? limits = null,
        ReadOnlySpan<DrmFormat> extraFormats = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(buffers);
        _sharedMemory = sharedMemory;
        _registry = buffers;
        _limits = limits ?? new ShmLimits();
        _formats = [DrmFormat.Argb8888, DrmFormat.Xrgb8888, .. extraFormats];
        Policy = policy == ShmAccessPolicy.Guarded && (sharedMemory is null || !ProbeGuard(sharedMemory))
            ? ShmAccessPolicy.Direct
            : policy;
        _global = display.CreateGlobal(WlShm.Interface, Version, OnBind);
    }

    public ShmAccessPolicy Policy { get; }

    private ISharedMemory? SharedMemoryFor(WlClient client) =>
        client.FdSlots is { } slots ? new TokenSharedMemory(slots) : _sharedMemory;

    private static bool ProbeGuard(ISharedMemory sharedMemory)
    {
        Span<byte> source = [1, 2, 3, 4, 5, 6, 7, 8];
        Span<byte> destination = stackalloc byte[8];
        unsafe
        {
            fixed (byte* src = source)
            fixed (byte* dst = destination)
            {
                if (sharedMemory.TryCopyRows((nint)dst, 8, (nint)src, 8, 8, 1) && destination.SequenceEqual(source))
                {
                    return true;
                }
            }
        }

        Diagnostics.BasinLog.Warn(
            $"wl_shm: the guarded-copy primitive is unavailable (seccomp?); falling back to direct access");
        return false;
    }

    public void Dispose()
    {
        foreach (var state in _clients.Values)
        {
            foreach (var pool in state.Pools.ToArray())
            {
                pool.Dispose();
            }
        }

        _clients.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var shm = new WlShmResource(client, version, id);
        foreach (var format in _formats)
        {
            shm.SendFormat((WlShm.Format)format.ToWlShm());
        }

        shm.CreatePool += (_, e) =>
        {
            var state = StateOf(client);
            if (e.Size <= 0)
            {
                client.CloseFd(e.Fd);
                shm.PostError((uint)WlShm.Error.InvalidStride, $"invalid pool size {e.Size}");
                return;
            }

            if (state.PoolCount >= _limits.MaxPools)
            {
                client.CloseFd(e.Fd);
                shm.PostError((uint)WlShm.Error.InvalidFd, $"too many pools (limit {_limits.MaxPools})");
                return;
            }

            if (state.MappedBytes + e.Size > _limits.MaxBytes)
            {
                client.CloseFd(e.Fd);
                shm.PostError((uint)WlShm.Error.InvalidFd, $"pool exceeds the {_limits.MaxBytes}-byte cap");
                return;
            }

            if (SharedMemoryFor(client) is not { } memory)
            {
                client.CloseFd(e.Fd);
                shm.PostError(
                    (uint)WlShm.Error.InvalidFd,
                    "this host maps no client pool fd; a client reaches it over a transport that carries its own");
                return;
            }

            IMappedMemory mapping;
            try
            {
                mapping = memory.Map(e.Fd, e.Size);
            }
            catch (Exception ex) when (ex is WaylandException or ArgumentOutOfRangeException)
            {
                shm.PostError((uint)WlShm.Error.InvalidFd, $"create_pool failed: {ex.Message}");
                return;
            }

            state.SharedMemory = memory;
            var pool = new ShmPool(mapping, freed => state.Pools.Remove(freed));
            state.PoolCount++;
            state.MappedBytes += e.Size;
            state.Pools.Add(pool);

            var entry = new PoolEntry { Pool = pool, AccountedBytes = e.Size };
            var poolResource = new WlShmPoolResource(client, shm.Version, e.Id);
            WirePool(client, poolResource, entry, state);
        };
    }

    private void WirePool(WlClient client, WlShmPoolResource poolResource, PoolEntry entry, ClientState state)
    {
        poolResource.CreateBuffer += (_, e) =>
        {
            if (state.BufferCount >= _limits.MaxBuffers)
            {
                poolResource.PostError((uint)WlShm.Error.InvalidFd, $"too many buffers (limit {_limits.MaxBuffers})");
                return;
            }

            var format = DrmFormatExtensions.FromWlShm((uint)e.Format);
            if (Array.IndexOf(_formats, format) < 0)
            {
                poolResource.PostError((uint)WlShm.Error.InvalidFormat, $"unsupported format {e.Format}");
                return;
            }

            if (e.Width <= 0 || e.Height <= 0 ||
                e.Stride < (long)e.Width * format.BytesPerPixel() ||
                !entry.Pool.TryGetRegion(e.Offset, e.Height, e.Stride, out nint _))
            {
                poolResource.PostError(
                    (uint)WlShm.Error.InvalidStride,
                    $"buffer {e.Width}x{e.Height} stride {e.Stride} offset {e.Offset} out of pool bounds ({entry.Pool.Size})");
                return;
            }

            var bufferResource = new WlBufferResource(client, 1, e.Id);
            var buffer = new ManagedShmBuffer(
                entry.Pool, e.Offset, e.Width, e.Height, e.Stride, format,
                Policy == ShmAccessPolicy.Guarded ? state.SharedMemory ?? _sharedMemory : null);
            state.BufferCount++;
            _registry.Register(bufferResource.RawHandle, buffer);
            buffer.Released += () =>
            {
                if (!bufferResource.IsDestroyed)
                {
                    bufferResource.SendRelease();
                }
            };
            bufferResource.Destroyed += (_, _) =>
            {
                state.BufferCount--;
                buffer.Destroy();
            };
        };

        poolResource.Resize += (_, e) =>
        {
            var newAccounted = Math.Max(e.Size > 0 ? e.Size : 0, entry.AccountedBytes);
            if (state.MappedBytes - entry.AccountedBytes + newAccounted > _limits.MaxBytes)
            {
                poolResource.PostError((uint)WlShm.Error.InvalidFd, $"resize exceeds the {_limits.MaxBytes}-byte cap");
                return;
            }

            try
            {
                entry.Pool.Resize(e.Size);
            }
            catch (WaylandException ex)
            {
                poolResource.PostError((uint)WlShm.Error.InvalidFd, $"resize failed: {ex.Message}");
                return;
            }

            state.MappedBytes += newAccounted - entry.AccountedBytes;
            entry.AccountedBytes = newAccounted;
        };

        poolResource.Destroyed += (_, _) =>
        {
            if (entry.Released)
            {
                return;
            }

            entry.Released = true;
            entry.Pool.Release();
            state.PoolCount--;
            state.MappedBytes -= entry.AccountedBytes;
        };
    }

    private ClientState StateOf(WlClient client)
    {
        if (!_clients.TryGetValue(client, out var state))
        {
            state = new ClientState();
            _clients[client] = state;
            client.Destroyed += () =>
            {
                foreach (var pool in state.Pools.ToArray())
                {
                    pool.Dispose();
                }

                _clients.Remove(client);
            };
        }

        return state;
    }
}
