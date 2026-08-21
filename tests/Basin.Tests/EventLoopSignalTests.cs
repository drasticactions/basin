using System.Runtime.InteropServices;
using Xunit;

namespace Basin.Tests;

public class EventLoopSignalTests
{
    private const int SigWinch = 28;

    [DllImport("libc", SetLastError = true)]
    private static extern nint pthread_self();

    [DllImport("libc", SetLastError = true)]
    private static extern int pthread_kill(nint thread, int signal);

    [DllImport("libc")]
    private static extern int kill(int pid, int signal);

    [DllImport("libc")]
    private static extern int getpid();

    [Fact]
    public void A_signal_source_runs_its_handler_on_the_dispatch_thread()
    {
        using var host = new CompositorTestHost();

        var handlerThread = -1;
        var signals = 0;
        var source = host.Loop.AddSignal(SigWinch, signal =>
        {
            Assert.Equal(SigWinch, signal);
            handlerThread = Environment.CurrentManagedThreadId;
            signals++;
        });

        Assert.Equal(0, pthread_kill(pthread_self(), SigWinch));

        for (var i = 0; i < 200 && signals == 0; i++)
        {
            host.Loop.Dispatch(5);
        }

        Assert.Equal(1, signals);
        Assert.Equal(Environment.CurrentManagedThreadId, handlerThread);

        source.Remove();
    }

    [Fact]
    public void A_process_directed_terminate_reaches_the_handler_instead_of_killing_the_process()
    {
        using var host = new CompositorTestHost();

        var signals = 0;
        var source = host.Loop.AddSignal(Signal.Terminate, signal =>
        {
            Assert.Equal(Signal.Terminate, signal);
            signals++;
        });

        Assert.Equal(0, kill(getpid(), Signal.Terminate));

        for (var i = 0; i < 200 && signals == 0; i++)
        {
            host.Loop.Dispatch(5);
        }

        Assert.Equal(1, signals);

        source.Remove();
    }
}
