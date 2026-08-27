using Basin.Backend.Hosted;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class HostedSessionTests
{
    private sealed class Hosted : IDisposable
    {
        public Hosted(CompositorTestHost host, int outputs = 2, int width = 160, int height = 120)
        {
            Host = host;
            Backend = new HostedBackend();
            Session = new HostedSession(host.Display, host.Loop);
            for (var i = 0; i < outputs; i++)
            {
                var output = Backend.CreateOutput(new OutputMode(width, height, 60_000));
                var sceneOutput = new SceneOutput(host.Scene, output);
                Outputs.Add(output);
                SceneOutputs.Add(sceneOutput);
                Targets.Add(new MemoryBuffer(width, height, DrmFormat.Xrgb8888));
                Session.AddOutput(sceneOutput);
            }
        }

        public CompositorTestHost Host { get; }

        public HostedBackend Backend { get; }

        public HostedSession Session { get; }

        public List<HostedOutput> Outputs { get; } = [];

        public List<SceneOutput> SceneOutputs { get; } = [];

        public List<MemoryBuffer> Targets { get; } = [];

        public int Tick()
        {
            Session.BeginFrame();
            var composited = 0;
            try
            {
                for (var i = 0; i < SceneOutputs.Count; i++)
                {
                    if (Session.CommitOutput(SceneOutputs[i], Host.Renderer, Targets[i], 0))
                    {
                        composited++;
                    }
                }

                if (composited > 0)
                {
                    Host.Scene.SendFrameDone(1);
                }
            }
            finally
            {
                Session.EndFrame();
            }

            return composited;
        }

        public void Dispose()
        {
            Session.Dispose();
            foreach (var sceneOutput in SceneOutputs)
            {
                sceneOutput.Dispose();
            }

            Backend.Dispose();
            foreach (var target in Targets)
            {
                target.Destroy();
            }
        }
    }

    [Fact]
    public void One_dispatch_serves_every_output_and_one_flush_ends_the_frame()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF204080));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        var done = 0;
        var callback = surface.Frame();
        callback.Done += (_, _) => done++;
        surface.Commit();
        host.Client.Display.Flush();

        Assert.Equal(2, hosted.Tick());
        host.PumpToClient();
        Assert.Equal(1, done);
    }

    [Fact]
    public void Frame_callbacks_survive_a_steady_run()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host, outputs: 1);

        var surface = host.Client.Compositor.CreateSurface();
        var done = 0;
        for (var frame = 0; frame < 30; frame++)
        {
            var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF204080u + (uint)frame));
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 64, 48);
            var callback = surface.Frame();
            callback.Done += (_, _) => done++;
            surface.Commit();
            host.Client.Display.Flush();
            hosted.Tick();
            host.PumpToClient();
            buffer.Proxy.Destroy();
        }

        Assert.Equal(30, done);
    }

    private sealed class RecordingSink : Capabilities.IFrameSink
    {
        public List<IOutput> Begun { get; } = [];

        public List<IOutput> Ended { get; } = [];

        public long LastPresentedNanos { get; private set; } = -1;

        public void BeginFrame(IOutput output, long predictedVblankNanos) => Begun.Add(output);

        public void EndFrame(IOutput output, long presentedNanos)
        {
            Ended.Add(output);
            LastPresentedNanos = presentedNanos;
        }
    }

    [Fact]
    public void The_frame_clock_is_ended_for_every_output_that_composited()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host, outputs: 2);
        var clock = new Capabilities.Defaults.FrameClock();
        var sink = new RecordingSink();
        clock.Add(sink);
        hosted.Session.Frames = clock;

        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(64, 48, Fill.Solid(64, 48, 0xFF204080));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 64, 48);
        surface.Commit();
        host.Client.Display.Flush();

        Assert.Equal(2, hosted.Tick());
        Assert.Equal(2, sink.Begun.Count);
        Assert.Equal(2, sink.Ended.Count);
        Assert.Equal(0, sink.LastPresentedNanos);

        sink.Ended.Clear();
        hosted.Session.BeginFrame();
        hosted.Session.EndFrame();
        Assert.Empty(sink.Ended);
    }

    [Fact]
    public void A_commit_outside_a_frame_still_composites()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host, outputs: 1);
        Assert.True(hosted.Session.CommitOutput(hosted.SceneOutputs[0], host.Renderer, hosted.Targets[0], 0));
        Assert.Throws<InvalidOperationException>(hosted.Session.EndFrame);
    }

    [Fact]
    public void A_second_begin_without_an_end_is_refused()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host, outputs: 1);
        hosted.Session.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => hosted.Session.BeginFrame());
        hosted.Session.EndFrame();
    }

    [Fact]
    public void The_watchdog_reports_a_slow_dispatch()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host, outputs: 1);
        hosted.Session.DispatchWarning = TimeSpan.Zero;
        var overran = 0;
        hosted.Session.DispatchOverran += _ => overran++;
        hosted.Tick();
        Assert.Equal(1, overran);
    }
}
