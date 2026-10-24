using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.GLib;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed unsafe class GLibContextSourceTests
{
    private const string GLib = "libglib-2.0.so.0";

    private static int s_idleFired;
    private static int s_timeoutFired;
    private static long s_idleAt;
    private static long s_timeoutAt;

    [Fact]
    public void A_glib_timeout_and_idle_fire_through_the_loop_with_no_polling_wait()
    {
        Assert.SkipUnless(NativeLibrary.TryLoad(GLib, out _), "libglib is not installed");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        var loop = new CompositorRunLoop(host) { DispatchTimeoutMillis = -1 };
        using var source = GLibContextSource.TryCreate(host.Loop, BasinLog.For("glib-test"));
        Assert.NotNull(source);
        loop.Iterating += source.BeforeWait;
        loop.Iterated += source.AfterWait;

        s_idleFired = 0;
        s_timeoutFired = 0;
        var guard = host.Loop.AddTimer(loop.Stop);
        guard.UpdateTimer(3000);
        var iterations = 0;
        loop.Iterated += () =>
        {
            iterations++;
            if (s_timeoutFired > 0)
            {
                loop.Stop();
            }
        };

        var started = Environment.TickCount64;
        _ = g_idle_add(&OnIdle, 0);
        _ = g_timeout_add(40, &OnTimeout, 0);
        loop.Run();
        guard.Remove();

        Assert.Equal(1, s_idleFired);
        Assert.Equal(1, s_timeoutFired);
        Assert.True(s_idleAt <= s_timeoutAt);
        Assert.InRange(s_timeoutAt - started, 35, 2000);
        Assert.InRange(iterations, 1, 10);
        Assert.True(source.Dispatches >= 2);
    }

    [Fact]
    public void An_idle_context_leaves_no_timer_armed()
    {
        Assert.SkipUnless(NativeLibrary.TryLoad(GLib, out _), "libglib is not installed");
        using var host = BasinHost.Create(HostOptions.ForBackend("headless"));
        using var source = GLibContextSource.TryCreate(host.Loop, BasinLog.For("glib-test"));
        Assert.NotNull(source);

        s_timeoutFired = 0;
        _ = g_timeout_add(5, &OnTimeout, 0);
        source.BeforeWait();
        Assert.True(source.TimerArmed);
        var deadline = Environment.TickCount64 + 2000;
        while (s_timeoutFired == 0 && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(100);
            source.AfterWait();
            source.BeforeWait();
        }

        Assert.Equal(1, s_timeoutFired);
        Assert.False(source.TimerArmed);
        Assert.True(source.WatchedDescriptors >= 1);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnIdle(nint data)
    {
        s_idleFired++;
        s_idleAt = Environment.TickCount64;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnTimeout(nint data)
    {
        s_timeoutFired++;
        s_timeoutAt = Environment.TickCount64;
        return 0;
    }

    [DllImport(GLib)]
    private static extern uint g_idle_add(delegate* unmanaged[Cdecl]<nint, int> function, nint data);

    [DllImport(GLib)]
    private static extern uint g_timeout_add(uint interval, delegate* unmanaged[Cdecl]<nint, int> function, nint data);
}
