using System.Runtime.InteropServices;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class DrmLeaseTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Lease_lifecycle_grant_revoke_withdraw()
    {
        using var host = new CompositorTestHost();
        var device = new TestDrmLeaseDevice();
        using var manager = new DrmLeaseManager(host.Display, device);

        var vrConnector = manager.OfferConnector("DP-2", "VR headset", 77, [77, 101, 102]);

        Basin.Desktop.Protocol.WpDrmLeaseDeviceV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_drm_lease_device_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpDrmLeaseDeviceV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var drmFd = -1;
        Basin.Desktop.Protocol.WpDrmLeaseConnectorV1? connectorProxy = null;
        string? connectorName = null;
        var connectorId = 0u;
        var deviceDone = false;
        proxy!.DrmFd += (_, e) =>
        {
            drmFd = e.Fd;
        };
        proxy.Connector += (_, e) =>
        {
            connectorProxy = e.Id;
            connectorProxy.Name += (_, ne) => connectorName = ne.Name;
            connectorProxy.ConnectorId += (_, ce) => connectorId = ce.ConnectorId;
        };
        proxy.Done += (_, _) => deviceDone = true;
        host.PumpUntil(() => deviceDone && connectorProxy is not null);
        Assert.True(drmFd >= 0);
        close(drmFd);
        Assert.Equal("DP-2", connectorName);
        Assert.Equal(77u, connectorId);

        var request = proxy.CreateLeaseRequest();
        request.RequestConnector(connectorProxy!);
        var lease = request.Submit();
        var leaseFd = -1;
        var finished = false;
        lease.LeaseFd += (_, e) => leaseFd = e.LeasedFd;
        lease.Finished += (_, _) => finished = true;
        host.PumpUntil(() => leaseFd >= 0 || finished);
        Assert.True(leaseFd >= 0);
        close(leaseFd);
        Assert.Equal(new uint[] { 77, 101, 102 }, Assert.Single(device.LeaseRequests));
        Assert.Single(manager.Leases);
        Assert.Equal(42u, manager.Leases[0].LesseeId);

        manager.Leases[0].Revoke();
        host.PumpUntil(() => finished);
        Assert.Equal(42u, Assert.Single(device.Revoked));
        Assert.Empty(manager.Leases);

        var withdrawn = false;
        connectorProxy!.Withdrawn += (_, _) => withdrawn = true;
        manager.WithdrawConnector(vrConnector);
        host.PumpUntil(() => withdrawn);

        lease.Dispose();
        connectorProxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_lease_the_device_ends_by_itself_reaches_the_client()
    {
        using var host = new CompositorTestHost();
        var device = new TestDrmLeaseDevice();
        using var manager = new DrmLeaseManager(host.Display, device);
        device.Offer(new Basin.Capabilities.LeasableConnector("DP-2", "VR headset", 77, [77, 101, 102]));

        var (proxy, connectorProxy) = Bind(host);
        var request = proxy.CreateLeaseRequest();
        request.RequestConnector(connectorProxy);
        var lease = request.Submit();
        var leaseFd = -1;
        var finished = false;
        lease.LeaseFd += (_, e) => leaseFd = e.LeasedFd;
        lease.Finished += (_, _) => finished = true;
        host.PumpUntil(() => leaseFd >= 0 || finished);
        Assert.True(leaseFd >= 0);
        close(leaseFd);
        Assert.Single(manager.Leases);

        device.EndLease(manager.Leases[0].LesseeId);
        host.PumpUntil(() => finished);
        Assert.Empty(manager.Leases);
        Assert.Empty(device.Revoked);

        lease.Dispose();
        connectorProxy.Dispose();
        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void The_device_decides_what_is_offered()
    {
        using var host = new CompositorTestHost();
        var device = new TestDrmLeaseDevice();
        using var manager = new DrmLeaseManager(host.Display, device);

        Basin.Desktop.Protocol.WpDrmLeaseDeviceV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_drm_lease_device_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpDrmLeaseDeviceV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        var connectors = new List<Basin.Desktop.Protocol.WpDrmLeaseConnectorV1>();
        var names = new List<string>();
        var withdrawn = 0;
        proxy!.DrmFd += (_, e) => close(e.Fd);
        proxy.Connector += (_, e) =>
        {
            connectors.Add(e.Id);
            e.Id.Name += (_, ne) => names.Add(ne.Name);
            e.Id.Withdrawn += (_, _) => withdrawn++;
        };
        var done = false;
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.Empty(connectors);

        device.Offer(new Basin.Capabilities.LeasableConnector("DP-3", "Index HMD", 88, [88, 103, 104]));
        host.PumpUntil(() => names.Count == 1);
        Assert.Equal("DP-3", names[0]);

        device.Withdraw(88);
        host.PumpUntil(() => withdrawn == 1);

        foreach (var connector in connectors)
        {
            connector.Dispose();
        }

        proxy.Dispose();
        host.PumpToServer();
    }

    private static (Basin.Desktop.Protocol.WpDrmLeaseDeviceV1 Device, Basin.Desktop.Protocol.WpDrmLeaseConnectorV1 Connector) Bind(
        CompositorTestHost host)
    {
        Basin.Desktop.Protocol.WpDrmLeaseDeviceV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_drm_lease_device_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpDrmLeaseDeviceV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        Basin.Desktop.Protocol.WpDrmLeaseConnectorV1? connector = null;
        var done = false;
        proxy!.DrmFd += (_, e) => close(e.Fd);
        proxy.Connector += (_, e) => connector = e.Id;
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done && connector is not null);
        return (proxy, connector!);
    }
}

public sealed class DrmLeaseDeviceTests
{
    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Announcing_the_same_connectors_twice_says_nothing()
    {
        using var host = new CompositorTestHost();
        using var session = new Basin.Session.DirectSession();
        var backend = new Basin.Backend.Drm.DrmBackend(host.Loop, session, "/dev/dri/card0");

        var announcements = 0;
        backend.Leasing.ConnectorsChanged += () => announcements++;

        List<Basin.Capabilities.LeasableConnector> Offer() =>
            [new("DP-2", "Index HMD", 77, [77, 101, 102])];
        backend.Leasing.SetConnectors(Offer());
        backend.Leasing.SetConnectors(Offer());
        Assert.Equal(1, announcements);

        backend.Leasing.SetConnectors([new("DP-2", "Index HMD", 77, [77, 105, 106])]);
        Assert.Equal(2, announcements);

        backend.Leasing.SetConnectors([]);
        Assert.Equal(3, announcements);

        var buffer = new Basin.Capabilities.LeasableConnector[4];
        Assert.Equal(0, backend.Leasing.EnumerateConnectors(buffer));
    }

    [Fact]
    public void A_span_too_small_to_hold_the_offer_is_refused_rather_than_truncated()
    {
        using var host = new CompositorTestHost();
        using var session = new Basin.Session.DirectSession();
        var backend = new Basin.Backend.Drm.DrmBackend(host.Loop, session, "/dev/dri/card0");
        backend.Leasing.SetConnectors(
        [
            new("DP-2", "Index HMD", 77, [77, 101, 102]),
            new("DP-3", "Vive", 88, [88, 103, 104]),
        ]);

        Assert.Equal(-1, backend.Leasing.EnumerateConnectors(new Basin.Capabilities.LeasableConnector[1]));
        var buffer = new Basin.Capabilities.LeasableConnector[2];
        Assert.Equal(2, backend.Leasing.EnumerateConnectors(buffer));
        Assert.Equal(77u, buffer[0].ConnectorId);
        Assert.Equal(88u, buffer[1].ConnectorId);
    }

    [Fact]
    public void The_enumeration_fd_is_a_non_master_view_of_the_card()
    {
        Assert.SkipUnless(File.Exists("/dev/dri/card0"), "no DRM card to enumerate");
        using var host = new CompositorTestHost();
        using var session = new Basin.Session.DirectSession();

        var backend = new Basin.Backend.Drm.DrmBackend(host.Loop, session, "/dev/dri/card0");
        var fd = backend.Leasing.OpenEnumerationFd();
        Assert.SkipWhen(fd < 0, "no permission to open /dev/dri/card0");
        try
        {
            Assert.Equal(0, Drm.Native.Libdrm.drmIsMaster(fd));
        }
        finally
        {
            close(fd);
        }
    }

    [Fact]
    public void A_card_that_was_never_named_hands_out_no_fd()
    {
        using var host = new CompositorTestHost();
        using var session = new Basin.Session.DirectSession();
        var backend = new Basin.Backend.Drm.DrmBackend(host.Loop, session);
        Assert.Equal(-1, backend.Leasing.OpenEnumerationFd());
    }
}
