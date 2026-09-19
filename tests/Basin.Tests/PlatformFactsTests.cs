using System.Runtime.InteropServices;
using Xunit;

namespace Basin.Tests;

public sealed class PlatformFactsTests
{
    private static string Platform
    {
        get
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var dash = rid.IndexOf('-');
            return dash < 0 ? rid : rid[..dash];
        }
    }

    private static readonly Dictionary<string, (bool Descriptors, bool LocalClients, bool Threads, bool XkbData, bool HostLayout, bool Syncobj, bool Signals)> Table = new()
    {
        ["linux"] = (true, true, true, true, false, true, true),
        ["osx"] = (true, true, true, false, true, false, true),
        ["win"] = (false, false, true, false, true, false, false),
        ["ios"] = (true, false, true, false, false, false, true),
        ["iossimulator"] = (true, false, true, false, false, false, true),
        ["maccatalyst"] = (true, false, true, false, false, false, true),
        ["android"] = (true, false, true, false, false, false, true),
        ["browser"] = (false, false, false, false, false, false, false),
    };

    [Fact]
    public void The_facts_match_the_host()
    {
        var platform = Platform;
        Assert.True(Table.TryGetValue(platform, out var expected), $"no fact row for platform '{platform}'");
        Assert.Equal(expected.Descriptors, PlatformFacts.HasDescriptors);
        Assert.Equal(expected.LocalClients, PlatformFacts.HasLocalClients);
        Assert.Equal(expected.Threads, PlatformFacts.HasThreads);
        Assert.Equal(expected.XkbData, PlatformFacts.HasSystemXkbData);
        Assert.Equal(expected.HostLayout, PlatformFacts.HasHostKeyboardLayout);
        Assert.Equal(expected.Syncobj, PlatformFacts.HasSyncobj);
        Assert.Equal(expected.Signals, PlatformFacts.HasPosixSignals);
    }

    [Fact]
    public void The_kernel_facts_are_exclusive()
    {
        Assert.False(PlatformFacts.HasLinuxSyscalls && PlatformFacts.HasDarwinSyscalls);
        Assert.Equal(OperatingSystem.IsLinux() || OperatingSystem.IsAndroid(), PlatformFacts.HasLinuxSyscalls);
        Assert.Equal(
            OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS(),
            PlatformFacts.HasDarwinSyscalls);
    }

    [Fact]
    public void A_host_without_local_clients_takes_the_managed_transport()
    {
        var options = new Basin.Hosted.BasinCompositorOptions();
        Assert.Equal(!PlatformFacts.HasLocalClients || !OperatingSystem.IsLinux(), options.ManagedTransport);
    }
}
