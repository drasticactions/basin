using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class ClientSourceTests
{
    private static bool Missing(bool linux, bool windows, string? ssh = null, string? listen = null, string? command = null) =>
        Program.MissingClientSource(linux, windows, ssh, listen, command);

    [Fact]
    public void A_bare_linux_run_binds_a_socket_and_waits()
    {
        Assert.False(Missing(linux: true, windows: false));
        Assert.False(Missing(linux: true, windows: false, command: "foot"));
    }

    [Fact]
    public void A_bare_run_off_linux_has_nowhere_for_a_client_to_come_from()
    {
        Assert.True(Missing(linux: false, windows: false));
        Assert.True(Missing(linux: false, windows: true));
    }

    [Fact]
    public void A_local_command_is_a_client_source_on_macos_and_never_on_windows()
    {
        Assert.False(Missing(linux: false, windows: false, command: "foot"));
        Assert.True(Missing(linux: false, windows: true, command: "foot"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Either_remote_flag_opens_the_session(bool linux, bool windows)
    {
        Assert.False(Missing(linux, windows, ssh: "lab"));
        Assert.False(Missing(linux, windows, listen: "/tmp/basin-wp.sock"));
    }
}
