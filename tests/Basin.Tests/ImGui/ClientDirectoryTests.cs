using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Xunit;

namespace Basin.Tests;

public sealed class ClientDirectoryTests
{
    [Fact]
    public void A_connected_client_is_listed_with_its_credentials_and_its_objects()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server");
        using var host = new CompositorTestHost();
        using var directory = new DisplayClientDirectory(host.Display);
        var surface = host.Client.Compositor.CreateSurface();
        host.PumpToServer();

        Span<ClientInfo> clients = new ClientInfo[8];
        var count = directory.Enumerate(clients);
        Assert.True(count >= 1, "the connected test client is not listed");
        var client = clients[0];
        Assert.Equal(Environment.ProcessId, client.Pid);
        Assert.True(client.Id > 0);

        Assert.True(directory.CanListObjects);
        var objects = new ClientObjectInfo[64];
        var kinds = directory.Objects(client.Id, objects);
        Assert.True(kinds > 0);
        var names = objects.Take(kinds).ToDictionary(o => (o.Interface, o.Version), o => o.Count);
        Assert.Contains(names.Keys, key => key.Interface == "wl_compositor");
        Assert.Contains(names.Keys, key => key.Interface == "wl_surface");
        Assert.Contains(names.Keys, key => key.Interface == "wl_display");

        Assert.Equal(-1, directory.Objects(client.Id, new ClientObjectInfo[1]));
        Assert.Equal(-1, directory.Enumerate(Span<ClientInfo>.Empty));
        Assert.Equal(0, directory.Objects(9999, objects));
        surface.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void A_client_that_disconnects_leaves_the_directory()
    {
        Assert.SkipWhen(!CompositorTestHost.HasWaylandServer, "this host has no libwayland server");
        using var host = new CompositorTestHost();
        using var directory = new DisplayClientDirectory(host.Display);
        var before = directory.Count;
        _ = host.ConnectClient();
        host.PumpToServer();
        Assert.Equal(before + 1, directory.Count);
        var clients = new ClientInfo[8];
        var count = directory.Enumerate(clients);
        clients[count - 1].Client.Destroy();
        for (var i = 0; i < 50 && directory.Count > before; i++)
        {
            host.Loop.Dispatch(10);
        }

        Assert.Equal(before, directory.Count);
    }
}
