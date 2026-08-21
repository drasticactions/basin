using Basin.Backend.Hosted;
using Basin.Diagnostics;
using Basin.Scene;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class HostedTickTests
{
    private sealed class Hosted : IDisposable
    {
        public Hosted(CompositorTestHost host, int width = 160, int height = 120)
        {
            Host = host;
            Backend = new HostedBackend();
            Output = Backend.CreateOutput(new OutputMode(width, height, 60_000));
            SceneOutput = new SceneOutput(host.Scene, Output);
            Frame = new HostedFrame(host.Display, host.Loop, SceneOutput);
            Target = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        }

        public CompositorTestHost Host { get; }

        public HostedBackend Backend { get; }

        public HostedOutput Output { get; }

        public SceneOutput SceneOutput { get; }

        public HostedFrame Frame { get; }

        public MemoryBuffer Target { get; }

        public bool Tick(int age = 0) => Frame.Tick(Host.Renderer, Target, age);

        public void Dispose()
        {
            Frame.Dispose();
            SceneOutput.Dispose();
            Backend.Dispose();
            Target.Destroy();
        }
    }

    [Fact]
    public void A_frame_callback_fires_in_the_tick_that_drew_it()
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

        hosted.Frame.BeforeDispatch += _ => host.Scene.SendFrameDone(1);
        Assert.True(hosted.Tick());

        host.Client.Display.Dispatch();
        Assert.Equal(1, done);

        callback.Dispose();
        surface.Destroy();
    }

    [Fact]
    public void Age_zero_repaints_the_whole_output_and_a_real_age_does_not()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var red = new RenderColor(1f, 0f, 0f, 1f);
        var green = new RenderColor(0f, 1f, 0f, 1f);
        var blue = new RenderColor(0f, 0f, 1f, 1f);

        var node = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        node.SetPosition(4, 4);
        Assert.True(hosted.Frame.Tick(host.Renderer, hosted.Target, 0, new SceneCommitOptions { Background = red }));
        Assert.Equal(0xFF0000u, FarCorner(hosted.Target));

        node.SetPosition(8, 8);
        Assert.True(hosted.Frame.Tick(host.Renderer, hosted.Target, 1, new SceneCommitOptions { Background = green }));
        Assert.Equal(0xFF0000u, FarCorner(hosted.Target));

        node.SetPosition(12, 12);
        Assert.True(hosted.Frame.Tick(host.Renderer, hosted.Target, 0, new SceneCommitOptions { Background = blue }));
        Assert.Equal(0x0000FFu, FarCorner(hosted.Target));

        node.Destroy();
    }

    private static uint FarCorner(MemoryBuffer target)
    {
        var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(target);
        var offset = (((target.Height - 1) * target.Width) + target.Width - 1) * 4;
        return ((uint)rgba[offset] << 16) | ((uint)rgba[offset + 1] << 8) | rgba[offset + 2];
    }

    [Fact]
    public void An_undamaged_scene_costs_no_composite()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var node = new SceneRect(host.Scene.Root, 20, 20, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
        Assert.True(hosted.Tick());
        Assert.Equal(1, hosted.Frame.Composited);

        Assert.False(hosted.Tick(age: 1));
        Assert.False(hosted.Frame.NeedsRepaint);
        Assert.Equal(1, hosted.Frame.Composited);

        node.SetPosition(30, 30);
        Assert.True(hosted.Frame.NeedsRepaint);
        Assert.True(hosted.Tick(age: 1));
        Assert.Equal(2, hosted.Frame.Composited);

        node.Destroy();
    }

    [Fact]
    public void A_client_request_reaches_the_compositor_inside_the_tick()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var surface = host.Client.Compositor.CreateSurface();
        surface.Commit();
        host.Client.Display.Flush();

        Assert.Empty(host.SurfaceScenes);
        hosted.Tick();
        Assert.Single(host.SurfaceScenes);

        surface.Destroy();
        host.Client.Display.Flush();
        hosted.Tick();
    }

    [Fact]
    public void A_hosted_output_asks_the_host_for_a_tick_rather_than_emitting_one()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var requested = 0;
        var frames = 0;
        hosted.Output.FrameRequested += () => requested++;
        hosted.Output.Frame += () => frames++;

        hosted.Output.RequestFrame();
        Assert.Equal(1, requested);
        Assert.Equal(0, frames);

        hosted.Output.NotifyFrame();
        Assert.Equal(1, frames);
    }

    [Fact]
    public void Resizing_matches_the_output_to_the_host_surface()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        hosted.Output.Resize(320, 200, 2);
        Assert.Equal(320, hosted.Output.CurrentMode.Width);
        Assert.Equal(200, hosted.Output.CurrentMode.Height);
        Assert.Equal(2, hosted.Output.Scale);
    }

    [Fact]
    public void A_dispatch_that_holds_the_host_is_reported_and_then_blamed()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        var overran = new List<TimeSpan>();
        var exceeded = new List<TimeSpan>();
        hosted.Frame.DispatchWarning = TimeSpan.Zero;
        hosted.Frame.DispatchLimit = TimeSpan.FromDays(1);
        hosted.Frame.DispatchOverran += spent => overran.Add(spent);
        hosted.Frame.DispatchExceededLimit += spent => exceeded.Add(spent);

        hosted.Tick();
        Assert.Single(overran);
        Assert.Empty(exceeded);

        hosted.Frame.DispatchLimit = TimeSpan.Zero;
        hosted.Tick();
        Assert.Single(overran);
        Assert.Single(exceeded);
    }

    [Fact]
    public void A_graph_built_on_another_thread_is_rejected_rather_than_raced()
    {
        using var host = new CompositorTestHost();
        using var hosted = new Hosted(host);

        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                hosted.Tick();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thread.Start();
        thread.Join();

#if DEBUG
        Assert.NotNull(thrown);
        Assert.Contains("thread", thrown!.Message, StringComparison.OrdinalIgnoreCase);
#else
        Assert.SkipWhen(thrown is null, "ThreadAffinity asserts in Debug builds only");
#endif
    }

    [Fact]
    public void A_thread_adopting_an_affinity_acts_as_its_owner_for_the_scope()
    {
        var affinity = ThreadAffinity.Capture();
        Exception? insideScope = null;
        Exception? outsideScope = null;
        ThreadAffinity capturedUnderAdoption = default;
        var thread = new Thread(() =>
        {
            try
            {
                using (affinity.Adopt())
                {
                    affinity.Assert();
                    capturedUnderAdoption = ThreadAffinity.Capture();
                }
            }
            catch (Exception error)
            {
                insideScope = error;
            }

            try
            {
                affinity.Assert();
            }
            catch (Exception error)
            {
                outsideScope = error;
            }
        });

        thread.Start();
        thread.Join();

        Assert.Null(insideScope);
        capturedUnderAdoption.Assert();
#if DEBUG
        Assert.NotNull(outsideScope);
        Assert.Contains("thread", outsideScope!.Message, StringComparison.OrdinalIgnoreCase);
#else
        Assert.SkipWhen(outsideScope is null, "ThreadAffinity asserts in Debug builds only");
#endif
    }
}
