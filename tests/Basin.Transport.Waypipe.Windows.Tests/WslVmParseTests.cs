using Xunit;

#pragma warning disable CA1416

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class WslVmParseTests
{
    private static readonly Guid Expected = Guid.Parse("5045efd0-d9fd-4a25-ba73-4a7def558be6");

    [Theory]
    [InlineData("5045EFD0-D9FD-4A25-BA73-4A7DEF558BE6\n")]
    [InlineData("5045EFD0-D9FD-4A25-BA73-4A7DEF558BE6\r\n")]
    [InlineData("﻿5045EFD0-D9FD-4A25-BA73-4A7DEF558BE6\r\n")]
    [InlineData("{5045efd0-d9fd-4a25-ba73-4a7def558be6}")]
    [InlineData("wsl: a notice first\r\n5045EFD0-D9FD-4A25-BA73-4A7DEF558BE6\r\nand a line after\r\n")]
    public void The_first_guid_shaped_token_is_the_vm_id(string output)
    {
        Assert.True(WslVm.TryParseId(output, out var id));
        Assert.Equal(Expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("wslinfo: unrecognized option '--vm-id'\n")]
    [InlineData("5045EFD0-D9FD-4A25-BA73")]
    public void Output_without_a_guid_has_no_id(string output)
    {
        Assert.False(WslVm.TryParseId(output, out var id));
        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void The_watch_polls_every_5_s_for_a_minute_after_a_stop_and_every_30_s_otherwise()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), HyperVWaypipeListener.WatchInterval(0));
        Assert.Equal(TimeSpan.FromSeconds(5), HyperVWaypipeListener.WatchInterval(Environment.TickCount64 - 1_000));
        Assert.Equal(TimeSpan.FromSeconds(30), HyperVWaypipeListener.WatchInterval(Environment.TickCount64 - 61_000));
    }
}
