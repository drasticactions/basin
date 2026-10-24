using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed unsafe class DescriptorWaitTests : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;

    public DescriptorWaitTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the wait is built on epoll");
        CompositorTestHost.SkipWithoutWaylandServer();
        BasinCounters.Reset();
        _display = WlServerDisplay.Create();
        _loop = new WaylandEventLoop(_display);
    }

    public void Dispose() => _display.Dispose();

    [Fact]
    public void An_armed_descriptor_fires_once_and_a_disarmed_one_never_fires()
    {
        var fired = 0;
        using var wait = DescriptorWait.TryCreate(_loop, () => fired++);
        Assert.NotNull(wait);
        var (first, firstWrite) = Pipe();
        var (second, secondWrite) = Pipe();
        try
        {
            Assert.True(wait.Arm(first));
            Assert.True(wait.IsArmed);
            _loop.Dispatch(0);
            Assert.Equal(0, fired);
            Write(firstWrite);
            _loop.Dispatch(100);
            Assert.Equal(1, fired);
            Assert.False(wait.IsArmed);
            _loop.Dispatch(0);
            Assert.Equal(1, fired);

            Assert.True(wait.Arm(second));
            Assert.Equal(second, wait.Fd);
            wait.Disarm();
            Write(secondWrite);
            _loop.Dispatch(50);
            Assert.Equal(1, fired);

            Assert.True(wait.Arm(first));
            Assert.True(wait.Arm(second));
            _loop.Dispatch(100);
            Assert.Equal(2, fired);
        }
        finally
        {
            foreach (var fd in new[] { first, firstWrite, second, secondWrite })
            {
                _ = close(fd);
            }
        }
    }

    [Fact]
    public void Arming_and_disarming_allocate_nothing()
    {
        using var wait = DescriptorWait.TryCreate(_loop, () => { });
        Assert.NotNull(wait);
        var (read, write) = Pipe();
        try
        {
            wait.Arm(read);
            wait.Disarm();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                wait.Arm(read);
                wait.Disarm();
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            _ = close(read);
            _ = close(write);
        }
    }

    private static (int Read, int Write) Pipe()
    {
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        return (fds[0], fds[1]);
    }

    private static void Write(int fd)
    {
        byte one = 1;
        Assert.Equal(1, (int)write(fd, &one, 1));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte* data, nint count);

    [DllImport("libc")]
    private static extern int close(int fd);
}
