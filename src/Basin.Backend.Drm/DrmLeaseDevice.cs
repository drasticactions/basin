using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Drm.Native;

namespace Basin.Backend.Drm;

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

    public event Action<uint>? LeaseRevoked;

    public event Action? ConnectorsChanged;

    public IReadOnlyList<uint> Lessees => [.. _leases.Select(l => l.LesseeId)];

    public int OpenEnumerationFd()
    {
        if (_backend.DevicePath.Length == 0)
        {
            return -1;
        }

        var fd = OpenPath(_backend.DevicePath, OpenReadWrite | OpenCloexec);
        if (fd < 0)
        {
            BasinLog.Warn(
                $"{_backend.DevicePath}: cannot open a lease enumeration fd " +
                $"(errno {Marshal.GetLastPInvokeError()}); clients see a card they cannot enumerate");
            return -1;
        }

        if (Libdrm.drmIsMaster(fd) != 0 && Libdrm.drmDropMaster(fd) != 0)
        {
            BasinLog.Warn($"{_backend.DevicePath}: lease enumeration fd is master and will not drop it; withholding it");
            _ = CloseFd(fd);
            return -1;
        }

        return fd;
    }

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
            BasinLog.Warn($"{_backend.DevicePath}: lease of {objectIds.Length} object(s) refused ({fd})");
            return false;
        }

        leaseFd = fd;
        lesseeId = id;
        _leases.Add((id, ConnectorsOf(objectIds)));
        BasinLog.Info($"{_backend.DevicePath}: leased {objectIds.Length} object(s) to lessee {id}");
        return true;
    }

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
            BasinLog.Debug($"{_backend.DevicePath}: revoking lessee {lesseeId} returned {rc}");
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
