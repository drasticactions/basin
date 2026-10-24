using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_drm_lease_device_v1</c> global. It offers DRM connectors to clients and grants leases through <see cref="IDrmLeaseDevice"/>.
/// </summary>
public sealed class DrmLeaseManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    private readonly WlGlobal _global;
    private readonly IDrmLeaseDevice? _device;
    private readonly List<OfferedConnector> _connectors = [];
    private readonly List<Binding> _bindings = [];
    private readonly List<ActiveLease> _leases = [];

    /// <summary>
    /// Describes a connector that the manager offers for lease.
    /// </summary>
    public sealed class OfferedConnector
    {
        /// <summary>
        /// Gets the connector name, for example <c>DP-2</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the text that describes the connector to a person.
        /// </summary>
        public required string Description { get; init; }

        /// <summary>
        /// Gets the DRM object id of the connector.
        /// </summary>
        public required uint ConnectorId { get; init; }

        /// <summary>
        /// Gets the DRM object ids that a lease of the connector includes, for example its CRTC and planes.
        /// </summary>
        public required uint[] ObjectIds { get; init; }

        internal bool Withdrawn;
    }

    /// <summary>
    /// Describes a lease that the manager granted to a client.
    /// </summary>
    public sealed class ActiveLease
    {
        private readonly DrmLeaseManager _owner;
        internal readonly WpDrmLeaseV1Resource Resource;

        internal ActiveLease(DrmLeaseManager owner, WpDrmLeaseV1Resource resource, uint lesseeId)
        {
            _owner = owner;
            Resource = resource;
            LesseeId = lesseeId;
        }

        /// <summary>
        /// Gets the DRM lessee id of the lease.
        /// </summary>
        public uint LesseeId { get; }

        /// <summary>
        /// Revokes the lease on the device, sends <c>finished</c> to the client and removes the lease from <see cref="Leases"/>.
        /// </summary>
        public void Revoke()
        {
            _owner._device?.RevokeLease(LesseeId);
            Finish();
        }

        internal void Finish()
        {
            if (!Resource.IsDestroyed)
            {
                Resource.SendFinished();
            }

            _owner._leases.Remove(this);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrmLeaseManager"/> class and creates the global. With a device, it offers the device's leasable connectors at once.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="device">The lease device, or <see langword="null"/> to offer only the connectors that <see cref="OfferConnector"/> adds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public DrmLeaseManager(WlServerDisplay display, IDrmLeaseDevice? device)
    {
        ArgumentNullException.ThrowIfNull(display);
        _device = device;
        _global = display.CreateGlobal(WpDrmLeaseDeviceV1.Interface, Version, OnBind);
        if (_device is { } live)
        {
            live.ConnectorsChanged += RefreshConnectors;
            live.LeaseRevoked += OnLeaseRevoked;
            RefreshConnectors();
        }
    }

    private void OnLeaseRevoked(uint lesseeId)
    {
        foreach (var lease in _leases.ToArray())
        {
            if (lease.LesseeId == lesseeId)
            {
                lease.Finish();
            }
        }
    }

    /// <summary>
    /// Gets the leases that are active now.
    /// </summary>
    public IReadOnlyList<ActiveLease> Leases => _leases;

    /// <summary>
    /// Stops following the device and removes the global. Active leases stay granted.
    /// </summary>
    public void Dispose()
    {
        if (_device is { } live)
        {
            live.ConnectorsChanged -= RefreshConnectors;
            live.LeaseRevoked -= OnLeaseRevoked;
        }

        _global.Dispose();
    }

    private void RefreshConnectors()
    {
        if (_device is not { } device)
        {
            return;
        }

        var buffer = new LeasableConnector[Math.Max(8, _connectors.Count * 2)];
        var count = device.EnumerateConnectors(buffer);
        while (count < 0)
        {
            buffer = new LeasableConnector[buffer.Length * 2];
            count = device.EnumerateConnectors(buffer);
        }

        var live = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var entry = buffer[i];
            live.Add(entry.ConnectorId);
            if (_connectors.Any(c => c.ConnectorId == entry.ConnectorId))
            {
                continue;
            }

            OfferConnector(entry.Name, entry.Description, entry.ConnectorId, entry.ObjectIds);
        }

        foreach (var connector in _connectors.ToList())
        {
            if (!live.Contains(connector.ConnectorId))
            {
                WithdrawConnector(connector);
            }
        }
    }

    /// <summary>
    /// Offers a connector to every bound client and to clients that bind later.
    /// </summary>
    /// <param name="name">The connector name.</param>
    /// <param name="description">The text that describes the connector.</param>
    /// <param name="connectorId">The DRM connector id.</param>
    /// <param name="objectIds">The DRM object ids that a lease includes.</param>
    /// <returns>The offer. Pass it to <see cref="WithdrawConnector"/> to end it.</returns>
    public OfferedConnector OfferConnector(string name, string description, uint connectorId, uint[] objectIds)
    {
        var connector = new OfferedConnector
        {
            Name = name,
            Description = description,
            ConnectorId = connectorId,
            ObjectIds = objectIds,
        };
        _connectors.Add(connector);
        foreach (var binding in Live())
        {
            binding.Announce(connector);
            binding.Device.SendDone();
        }

        return connector;
    }

    /// <summary>
    /// Ends an offer and sends <c>withdrawn</c> to every client that got it. Leases that already exist stay active.
    /// </summary>
    /// <param name="connector">The offer to end.</param>
    public void WithdrawConnector(OfferedConnector connector)
    {
        connector.Withdrawn = true;
        _connectors.Remove(connector);
        foreach (var binding in Live())
        {
            if (binding.Connectors.TryGetValue(connector, out var resource) && !resource.IsDestroyed)
            {
                resource.SendWithdrawn();
                resource.SendDone();
            }
        }
    }

    private IEnumerable<Binding> Live()
    {
        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            if (_bindings[i].Device.IsDestroyed)
            {
                _bindings.RemoveAt(i);
            }
            else
            {
                yield return _bindings[i];
            }
        }
    }

    internal sealed class Binding
    {
        public required WpDrmLeaseDeviceV1Resource Device;
        public required Dictionary<OfferedConnector, WpDrmLeaseConnectorV1Resource> Connectors;

        public void Announce(OfferedConnector connector)
        {
            var resource = new WpDrmLeaseConnectorV1Resource(Device.Client, Device.Version, 0);
            Device.SendConnector(resource);
            resource.SendName(connector.Name);
            resource.SendDescription(connector.Description);
            resource.SendConnectorId(connector.ConnectorId);
            resource.SendDone();
            Connectors[connector] = resource;
            resource.Destroyed += (_, _) => Connectors.Remove(connector);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var device = new WpDrmLeaseDeviceV1Resource(client, version, id);
        var binding = new Binding { Device = device, Connectors = [] };
        _bindings.Add(binding);

        var enumFd = _device?.OpenEnumerationFd() ?? -1;
        if (enumFd >= 0)
        {
            device.SendDrmFd(enumFd);
            close(enumFd);
        }

        foreach (var connector in _connectors)
        {
            binding.Announce(connector);
        }

        device.SendDone();

        device.CreateLeaseRequest += (_, e) =>
        {
            var request = new WpDrmLeaseRequestV1Resource(client, device.Version, e.Id);
            var requested = new List<OfferedConnector>();
            request.RequestConnector += (_, ce) =>
            {
                foreach (var (connector, resource) in binding.Connectors)
                {
                    if (resource.RawHandle == ce.ConnectorHandle && !connector.Withdrawn)
                    {
                        requested.Add(connector);
                    }
                }
            };
            request.Submit += (_, se) =>
            {
                var lease = new WpDrmLeaseV1Resource(client, device.Version, se.Id);
                var objects = new List<uint>();
                foreach (var connector in requested)
                {
                    objects.AddRange(connector.ObjectIds);
                }

                if (objects.Count == 0 || _device is null ||
                    !_device.TryCreateLease(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(objects), out var leaseFd, out var lesseeId))
                {
                    lease.SendFinished();
                    return;
                }

                lease.SendLeaseFd(leaseFd);
                close(leaseFd);
                var active = new ActiveLease(this, lease, lesseeId);
                _leases.Add(active);
                lease.Destroyed += (_, _) =>
                {
                    if (_leases.Remove(active))
                    {
                        _device.RevokeLease(lesseeId);
                    }
                };
            };
        };
    }
}
