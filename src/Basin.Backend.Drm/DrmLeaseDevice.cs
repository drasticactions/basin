using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Drm.Native;
using static Basin.Backend.Drm.DrmLog;

namespace Basin.Backend.Drm;

/// <summary>
/// Offers the non-desktop connectors of a <see cref="DrmBackend"/> for DRM leasing and tracks the leases that it created.
/// </summary>
public sealed unsafe class DrmLeaseDevice : IDrmLeaseDevice
{
    private const int OpenReadWrite = 0x2;
    private const int OpenCloexec = 0x80000;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenPath(string path, int flags);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    private readonly DrmBackend _backend;
    private readonly List<LeasableConnector> _connectors = [];
    private readonly List<(uint LesseeId, uint[] ConnectorIds)> _leases = [];

    internal DrmLeaseDevice(DrmBackend backend) => _backend = backend;

    /// <summary>
    /// Occurs when the device revokes a lease because a leased connector went away or the backend is disposed. The argument is the lessee id.
    /// It does not occur for <see cref="RevokeLease"/>.
    /// </summary>
    public event Action<uint>? LeaseRevoked;

    /// <summary>
    /// Occurs when a scan changes the set of leasable connectors.
    /// </summary>
    public event Action? ConnectorsChanged;

    /// <summary>
    /// Gets a snapshot of the lessee ids of the active leases.
    /// </summary>
    public IReadOnlyList<uint> Lessees => [.. _leases.Select(l => l.LesseeId)];

    /// <summary>
    /// Opens a new fd on the card that is not DRM master, so that a client can read the connectors.
    /// </summary>
    /// <returns>The new fd, which the caller owns, or -1 when the card cannot be opened or the fd cannot drop master.</returns>
    public int OpenEnumerationFd()
    {
        if (_backend.DevicePath.Length == 0)
        {
            return -1;
        }

        var fd = OpenPath(_backend.DevicePath, OpenReadWrite | OpenCloexec);
        if (fd < 0)
        {
            Log.Warn(
                $"{_backend.DevicePath}: lease enumeration fd does not open (errno {Marshal.GetLastPInvokeError()}), " +
                $"clients cannot enumerate the card");
            return -1;
        }

        if (Libdrm.drmIsMaster(fd) != 0 && Libdrm.drmDropMaster(fd) != 0)
        {
            Log.Warn($"{_backend.DevicePath}: lease enumeration fd does not drop DRM master, fd withheld");
            _ = CloseFd(fd);
            return -1;
        }

        return fd;
    }

    /// <summary>
    /// Copies the leasable connectors into a span.
    /// </summary>
    /// <param name="connectors">The destination.</param>
    /// <returns>The number of connectors copied, or -1 when the span is too small.</returns>
    public int EnumerateConnectors(Span<LeasableConnector> connectors)
    {
        if (_connectors.Count > connectors.Length)
        {
            return -1;
        }

        for (var i = 0; i < _connectors.Count; i++)
        {
            connectors[i] = _connectors[i];
        }

        return _connectors.Count;
    }

    /// <summary>
    /// Leases a set of KMS objects to a client.
    /// </summary>
    /// <param name="objectIds">The connector, CRTC and plane ids to lease.</param>
    /// <param name="leaseFd">The lease fd, which the caller owns, or -1 on failure.</param>
    /// <param name="lesseeId">The lessee id, or 0 on failure.</param>
    /// <returns><see langword="false"/> when the list is empty, the session is inactive, or the kernel refuses the lease.</returns>
    public bool TryCreateLease(ReadOnlySpan<uint> objectIds, out int leaseFd, out uint lesseeId)
    {
        leaseFd = -1;
        lesseeId = 0;
        if (objectIds.Length == 0 || !_backend.SessionActive)
        {
            return false;
        }

        uint id;
        int fd;
        fixed (uint* objects = objectIds)
        {
            fd = Libdrm.drmModeCreateLease(_backend.Device.Fd, objects, objectIds.Length, OpenCloexec, &id);
        }

        if (fd < 0)
        {
            Log.Warn($"{_backend.DevicePath}: lease of {objectIds.Length} object(s) refused (error {fd})");
            return false;
        }

        leaseFd = fd;
        lesseeId = id;
        _leases.Add((id, ConnectorsOf(objectIds)));
        Log.Info($"{_backend.DevicePath}: leased {objectIds.Length} object(s) to lessee {id}");
        return true;
    }

    /// <summary>
    /// Revokes a lease that this device created. An unknown lessee id does nothing.
    /// </summary>
    /// <param name="lesseeId">The lessee id from <see cref="TryCreateLease"/>.</param>
    public void RevokeLease(uint lesseeId)
    {
        if (_leases.RemoveAll(l => l.LesseeId == lesseeId) == 0)
        {
            return;
        }

        Revoke(lesseeId);
    }

    internal void SetConnectors(List<LeasableConnector> connectors)
    {
        var changed = connectors.Count != _connectors.Count;
        if (!changed)
        {
            for (var i = 0; i < connectors.Count; i++)
            {
                if (connectors[i].ConnectorId != _connectors[i].ConnectorId ||
                    !connectors[i].ObjectIds.AsSpan().SequenceEqual(_connectors[i].ObjectIds))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
        {
            return;
        }

        for (var i = _leases.Count - 1; i >= 0; i--)
        {
            var lease = _leases[i];
            if (lease.ConnectorIds.Any(id => !connectors.Any(c => c.ConnectorId == id)))
            {
                _leases.RemoveAt(i);
                Revoke(lease.LesseeId);
                LeaseRevoked?.Invoke(lease.LesseeId);
            }
        }

        _connectors.Clear();
        _connectors.AddRange(connectors);
        ConnectorsChanged?.Invoke();
    }

    internal void RevokeAll()
    {
        if (_leases.Count == 0)
        {
            return;
        }

        var revoked = _leases.ToArray();
        _leases.Clear();
        foreach (var (lesseeId, _) in revoked)
        {
            Revoke(lesseeId);
            LeaseRevoked?.Invoke(lesseeId);
        }
    }

    private void Revoke(uint lesseeId)
    {
        var rc = Libdrm.drmModeRevokeLease(_backend.Device.Fd, lesseeId);
        if (rc != 0)
        {
            Log.Debug($"{_backend.DevicePath}: revoking lessee {lesseeId} returned {rc}");
        }
    }

    private uint[] ConnectorsOf(ReadOnlySpan<uint> objectIds)
    {
        var covered = new List<uint>();
        foreach (var connector in _connectors)
        {
            if (objectIds.Contains(connector.ConnectorId))
            {
                covered.Add(connector.ConnectorId);
            }
        }

        return [.. covered];
    }
}
