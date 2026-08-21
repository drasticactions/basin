using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ViewporterTests
{
    [Fact]
    public void Destination_scales_the_surface()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(2, 2, Fill.Solid(2, 2, 0xFFFF0000));

        viewport.SetDestination(8, 6);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverSurface = host.SurfaceScenes[0].Surface;
        Assert.Equal((8, 6), (serverSurface.Current.Width, serverSurface.Current.Height));

        host.RenderFrame();
        Assert.Equal(0xFFFF0000u, host.Pixel(0, 0));
        Assert.Equal(0xFFFF0000u, host.Pixel(7, 5));
        Assert.Equal(0xFF000000u, host.Pixel(8, 6));
    }

    [Fact]
    public void Source_crops_the_buffer()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(4, 4, (data, stride) =>
        {
            unsafe
            {
                for (var y = 0; y < 4; y++)
                {
                    var row = (uint*)(data + y * stride);
                    for (var x = 0; x < 4; x++)
                    {
                        row[x] = x < 2 ? 0xFF0000FFu : 0xFF00FF00u;
                    }
                }
            }
        });

        viewport.SetSource(WlFixed.FromInt(2), WlFixed.FromInt(0), WlFixed.FromInt(2), WlFixed.FromInt(4));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var serverSurface = host.SurfaceScenes[0].Surface;
        Assert.Equal((2, 4), (serverSurface.Current.Width, serverSurface.Current.Height));

        host.RenderFrame();
        Assert.Equal(0xFF00FF00u, host.Pixel(0, 0));
        Assert.Equal(0xFF00FF00u, host.Pixel(1, 3));
        Assert.Equal(0xFF000000u, host.Pixel(2, 0));
    }

    [Fact]
    public void Crop_and_scale_compose()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF00FFFF));

        viewport.SetSource(WlFixed.FromInt(1), WlFixed.FromInt(1), WlFixed.FromInt(2), WlFixed.FromInt(2));
        viewport.SetDestination(10, 10);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xFF00FFFFu, host.Pixel(0, 0));
        Assert.Equal(0xFF00FFFFu, host.Pixel(9, 9));
        Assert.Equal(0xFF000000u, host.Pixel(10, 10));
    }

    [Fact]
    public void Viewport_destroy_restores_buffer_size()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        var buffer = host.Client.CreateBuffer(3, 3, Fill.Solid(3, 3, 0xFFFF00FF));

        viewport.SetDestination(9, 9);
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(9, host.SurfaceScenes[0].Surface.Current.Width);

        viewport.Dispose();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(3, host.SurfaceScenes[0].Surface.Current.Width);
    }

    [Fact]
    public void Negative_destination_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        var viewport = host.Client.Viewporter!.GetViewport(surface);
        viewport.SetDestination(-2, 5);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }

    [Fact]
    public void Second_viewport_on_a_surface_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        var surface = host.Client.Compositor.CreateSurface();
        _ = host.Client.Viewporter!.GetViewport(surface);
        host.PumpToServer();
        _ = host.Client.Viewporter.GetViewport(surface);
        host.PumpToServer();
        host.Display.FlushClients();

        Assert.ThrowsAny<Exception>(() =>
        {
            host.Client.Display.Dispatch();
            host.Client.Display.Roundtrip();
        });
    }
}

public sealed class PresentationTests
{
    [Fact]
    public void Committed_feedback_reports_presentation()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var feedback = client.Presentation!.Feedback(surface);
        ulong presentedTime = 0;
        uint presentedRefresh = 0;
        ulong presentedSeq = 0;
        var presented = false;
        feedback.Presented += (_, e) =>
        {
            presentedTime = (((ulong)e.TvSecHi << 32) | e.TvSecLo) * 1_000_000_000 + e.TvNsec;
            presentedRefresh = e.Refresh;
            presentedSeq = ((ulong)e.SeqHi << 32) | e.SeqLo;
            presented = true;
        };

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToClient();
        Assert.Equal(1u, client.PresentationClockId);

        var serverSurface = host.SurfaceScenes[0].Surface;
        host.Presentation.Presented(serverSurface, host.Output, 5_000_000_123, 16_666_666, 42, PresentedFlags.Vsync);
        host.PumpUntil(() => presented);

        Assert.Equal(5_000_000_123ul, presentedTime);
        Assert.Equal(16_666_666u, presentedRefresh);
        Assert.Equal(42ul, presentedSeq);
    }

    [Fact]
    public void Uncommitted_feedback_stays_pending()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var feedback = client.Presentation!.Feedback(surface);
        var resolved = false;
        feedback.Presented += (_, _) => resolved = true;
        feedback.Discarded += (_, _) => resolved = true;
        host.PumpToServer();

        var serverSurface = host.SurfaceScenes[0].Surface;
        host.Presentation.Presented(serverSurface, host.Output, 1, 1, 1, PresentedFlags.None);
        host.PumpToClient();
        Assert.False(resolved);

        surface.Commit();
        host.PumpToServer();
        host.Presentation.Presented(serverSurface, host.Output, 2, 1, 2, PresentedFlags.None);
        host.PumpUntil(() => resolved);
    }

    [Fact]
    public void Discarded_feedback_reports_discard()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var feedback = client.Presentation!.Feedback(surface);
        var discarded = false;
        feedback.Discarded += (_, _) => discarded = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.Presentation.Discarded(host.SurfaceScenes[0].Surface);
        host.PumpUntil(() => discarded);
    }

    [Fact]
    public void A_superseded_content_update_is_discarded_rather_than_presented()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var superseded = client.Presentation!.Feedback(surface);
        var supersededDiscarded = false;
        var supersededPresented = false;
        superseded.Discarded += (_, _) => supersededDiscarded = true;
        superseded.Presented += (_, _) => supersededPresented = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        var shown = client.Presentation!.Feedback(surface);
        var shownPresented = false;
        shown.Presented += (_, _) => shownPresented = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.Presentation.PresentAllNow(host.Output);
        host.PumpUntil(() => supersededDiscarded && shownPresented);

        Assert.False(supersededPresented);
    }

    [Fact]
    public void A_content_update_committed_after_the_frame_waits_for_the_next_one()
    {
        using var host = new CompositorTestHost();
        using var pump = new PresentationFeedbackPump(host.Presentation, host.Layout);
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var sampled = client.Presentation!.Feedback(surface);
        var sampledPresented = false;
        sampled.Presented += (_, _) => sampledPresented = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.RenderFrame();

        var late = client.Presentation!.Feedback(surface);
        var lateResolved = false;
        late.Presented += (_, _) => lateResolved = true;
        late.Discarded += (_, _) => lateResolved = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.Presentation.PresentAllNow(host.Output);
        host.PumpUntil(() => sampledPresented);

        Assert.False(lateResolved);

        host.RenderFrame();
        host.Presentation.PresentAllNow(host.Output);
        host.PumpUntil(() => lateResolved);
    }

    [Fact]
    public void Presented_feedback_names_the_output_it_was_synchronized_to()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var feedback = client.Presentation!.Feedback(surface);
        WlOutput? synced = null;
        var presented = false;
        feedback.SyncOutput += (_, e) => synced = e.Output;
        feedback.Presented += (_, _) => presented = true;

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.Presentation.PresentAllNow(host.Output);
        host.PumpUntil(() => presented);

        Assert.NotNull(synced);
        Assert.Same(client.Outputs[0], synced);
    }

    [Fact]
    public void The_reported_refresh_is_the_modes_own_rate()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var buffer = client.CreateBuffer(4, 4, Fill.Solid(4, 4, 0xFF123456));

        var feedback = client.Presentation!.Feedback(surface);
        uint presentedRefresh = 0;
        var presented = false;
        feedback.Presented += (_, e) =>
        {
            presentedRefresh = e.Refresh;
            presented = true;
        };

        surface.Attach(buffer.Proxy, 0, 0);
        surface.Commit();
        host.PumpToServer();

        host.Presentation.PresentAllNow(host.Output);
        host.PumpUntil(() => presented);

        Assert.Equal(16_666_666u, presentedRefresh);
        Assert.Equal(0u, new OutputMode(1920, 1080, 0).RefreshIntervalNanoseconds);
    }
}
