using System.Net.Sockets;
using Basin.Sdl;
using SDL3;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlLoopTests
{
    [Fact]
    public void Runs_on_the_offscreen_driver()
    {
        using var session = new SdlSession(host: false);
        Assert.Equal("offscreen", session.Loop.VideoDriver);
        Assert.NotEqual(0u, session.Loop.WakeEventType);
    }

    [Fact]
    public void An_empty_window_opens()
    {
        using var session = new SdlSession(host: false);
        using var window = new SdlWindow("empty", 320, 200, SDL.WindowFlags.Resizable);
        window.Show();
        Assert.NotEqual(0u, window.Id);
        Assert.Equal((320, 200), window.PixelSize);
    }

    [Fact]
    public void A_wake_from_another_thread_reaches_the_loop_once()
    {
        using var session = new SdlSession(host: false);
        var thread = new Thread(() =>
        {
            session.Loop.Wake();
            session.Loop.Wake();
        });
        thread.Start();
        thread.Join();

        Assert.True(session.PumpUntil(() => session.Loop.Wakes > 0));
        Assert.True(session.Loop.TakeActivity());
        session.Loop.Pump(20);
        Assert.Equal(1, session.Loop.Wakes);
        Assert.False(session.Loop.TakeActivity());
    }

    [Fact]
    public void A_posted_action_runs_on_the_loop_thread()
    {
        using var session = new SdlSession(host: false);
        var loopThread = Environment.CurrentManagedThreadId;
        var ranOn = -1;
        var thread = new Thread(() => session.Loop.Post(() => ranOn = Environment.CurrentManagedThreadId));
        thread.Start();
        thread.Join();

        Assert.True(session.PumpUntil(() => ranOn != -1));
        Assert.Equal(loopThread, ranOn);
    }

    [Fact]
    public void An_idle_loop_blocks_and_pending_work_does_not()
    {
        using var session = new SdlSession();
        session.Loop.TakeActivity();
        var idle = session.Loop.IdleTimeout();
        if (session.Host!.Wake.IsWatching)
        {
            Assert.Equal(-1, idle);
        }
        else
        {
            Assert.InRange(idle, 0, SdlLoop.UnwatchedPollMs);
        }

        session.Loop.Wake();
        Assert.Equal(0, session.Loop.IdleTimeout());
    }

    [Fact]
    public void A_connecting_client_wakes_the_loop()
    {
        using var session = new SdlSession();
        Assert.SkipUnless(session.Host!.Wake.IsWatching, "this host has no loop descriptor to watch");
        var path = Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, session.Host.Socket);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(path));

        Assert.True(session.PumpUntil(() => session.Loop.Wakes > 0));
    }

    [Fact]
    public void Quit_ends_the_frame_loop()
    {
        using var session = new SdlSession();
        using var frames = new SdlFrameLoop(session.Loop, session.Host!);
        var quit = default(SDL.Event);
        quit.Type = (uint)SDL.EventType.Quit;
        SDL.PushEvent(ref quit);

        Assert.False(frames.RunOnce());
        Assert.True(session.Loop.QuitRequested);
    }

    [Fact]
    public void A_wake_runs_exactly_one_pump()
    {
        using var session = new SdlSession();
        using var frames = new SdlFrameLoop(session.Loop, session.Host!);
        session.Loop.TakeActivity();
        session.Loop.Wake();
        Assert.True(frames.RunOnce());
        Assert.Equal(1, frames.Pumps);
    }
}
