using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public class GlobalNameTests
{
    [Fact]
    public void A_globals_name_is_the_one_its_client_was_advertised()
    {
        using var host = new CompositorTestHost();

        var advertised = new Dictionary<string, uint>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) => advertised[e.Interface] = e.Name;
        host.PumpToClient();

        var client = Assert.Single(host.Display.Clients);

        Assert.True(advertised.Count > 3, "expected several globals");
        Assert.Equal(advertised["wl_output"], host.OutputGlobal.NameFor(client));
        Assert.Equal(advertised["wl_seat"], host.Seat.NameFor(client));

        Assert.NotEqual(1u, host.OutputGlobal.NameFor(client));
    }

    [Fact]
    public void A_filtered_global_has_no_name_for_the_client_that_cannot_see_it()
    {
        using var host = new CompositorTestHost();

        var trusted = Assert.Single(host.Display.Clients);
        host.Display.SetGlobalFilter((client, _, interfaceName) =>
            interfaceName != "wl_output" || ReferenceEquals(client, trusted));

        var sandboxed = host.ConnectClient();
        var seen = new List<string>();
        var registry = sandboxed.Display.GetRegistry();
        registry.Global += (_, e) => seen.Add(e.Interface);
        host.PumpToClient();

        var sandboxedClient = host.Display.Clients.Single(c => !ReferenceEquals(c, trusted));
        Assert.DoesNotContain("wl_output", seen);
        Assert.Equal(0u, host.OutputGlobal.NameFor(sandboxedClient));

        Assert.NotEqual(0u, host.OutputGlobal.NameFor(trusted));

        host.Display.SetGlobalFilter(null);
        host.DisconnectClient(sandboxed);
    }

    [Fact]
    public void A_name_reported_to_a_client_is_one_it_can_bind()
    {
        using var host = new CompositorTestHost();

        var outputName = 0u;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_output")
            {
                outputName = e.Name;
            }
        };
        host.PumpToClient();

        var client = Assert.Single(host.Display.Clients);
        Assert.Equal(outputName, host.OutputGlobal.NameFor(client));

        var bound = registry.Bind<Wayland.WlOutput>(host.OutputGlobal.NameFor(client), 4);
        var boundName = string.Empty;
        bound.Name += (_, e) => boundName = e.Name;
        host.PumpUntil(() => boundName.Length > 0);
        Assert.Equal(host.Output.Name, boundName);

        bound.Dispose();
        host.PumpToServer();
    }
}
