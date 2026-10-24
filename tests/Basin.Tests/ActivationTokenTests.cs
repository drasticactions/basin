using Basin.Capabilities.Defaults;
using Xunit;

namespace Basin.Tests;

public sealed class ActivationTokenTests
{
    [Fact]
    public void A_token_redeems_once()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var tokens = new DefaultActivationTokens();
        var token = tokens.Mint(window.ServerSurface.Resource.Client, window.ServerSurface, 1, "app.one");

        Assert.True(tokens.Redeem(token, window.ServerSurface));
        Assert.Equal("app.one", tokens.LastRedeemedAppId);
        Assert.False(tokens.Redeem(token, window.ServerSurface));
        Assert.Equal(0, tokens.Outstanding);
    }

    [Fact]
    public void A_token_past_the_timeout_does_not_redeem()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var tokens = new DefaultActivationTokens { TimeoutMs = 0 };
        var token = tokens.Mint(window.ServerSurface.Resource.Client, window.ServerSurface, 1, null);

        Assert.False(tokens.Redeem(token, window.ServerSurface));
        Assert.Equal(0, tokens.Outstanding);
    }

    [Fact]
    public void A_full_table_drops_its_oldest_token()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var tokens = new DefaultActivationTokens { Capacity = 4 };
        var first = tokens.Mint(window.ServerSurface.Resource.Client, null, 1, null);
        for (var i = 0; i < 4; i++)
        {
            tokens.Mint(window.ServerSurface.Resource.Client, null, 1, null);
        }

        Assert.Equal(4, tokens.Outstanding);
        Assert.False(tokens.Redeem(first, window.ServerSurface));
    }

    [Fact]
    public void A_token_outlives_the_client_that_minted_it()
    {
        using var host = new CompositorTestHost();
        var launcher = host.ConnectClient();
        var launcherWindow = MappedToplevel.Map(host, launcher);
        var tokens = new DefaultActivationTokens();
        var token = tokens.Mint(launcherWindow.ServerSurface.Resource.Client, launcherWindow.ServerSurface, 1, "app.child");
        host.DisconnectClient(launcher);

        var child = MappedToplevel.Map(host, host.Client);
        Assert.True(tokens.Redeem(token, child.ServerSurface));
        Assert.Equal("app.child", tokens.LastRedeemedAppId);
    }

    [Fact]
    public void A_launch_token_reports_its_origin_when_redeemed()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var tokens = new DefaultActivationTokens();
        var seen = new List<ActivationRedemption>();
        tokens.Redeemed += seen.Add;
        var launch = tokens.MintLaunch("foot.desktop");
        var client = tokens.Mint(window.ServerSurface.Resource.Client, window.ServerSurface, 1, null);

        Assert.True(tokens.Redeem(launch, window.ServerSurface));
        Assert.True(tokens.Redeem(client, window.ServerSurface));

        Assert.Equal(2, seen.Count);
        Assert.True(seen[0].FromLaunch);
        Assert.Equal("foot.desktop", seen[0].AppId);
        Assert.Equal(0, seen[0].RequesterPid);
        Assert.Same(window.ServerSurface, seen[0].Surface);
        Assert.False(seen[1].FromLaunch);
        Assert.Equal(Environment.ProcessId, seen[1].RequesterPid);
    }
}
