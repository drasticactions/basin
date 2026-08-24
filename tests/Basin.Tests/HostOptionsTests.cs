using Basin.Host;
using Xunit;

namespace Basin.Tests;

public class HostOptionsTests
{
    [Theory]
    [InlineData("drm", HostBackend.Drm)]
    [InlineData("nested", HostBackend.Nested)]
    [InlineData("headless", HostBackend.Headless)]
    public void ForBackend_maps_the_name(string name, HostBackend expected)
    {
        var options = HostOptions.ForBackend(name);
        Assert.Equal(expected, options.Backend);
        Assert.Equal(-1, options.SocketFd);
    }

    [Fact]
    public void ForBackend_takes_an_inherited_socket_fd()
    {
        var options = HostOptions.ForBackend("nested:7");
        Assert.Equal(HostBackend.Nested, options.Backend);
        Assert.Equal(7, options.SocketFd);
    }

    [Fact]
    public void ForBackend_refuses_an_unknown_name()
    {
        Assert.Throws<ArgumentException>(() => HostOptions.ForBackend("x11"));
    }
}
