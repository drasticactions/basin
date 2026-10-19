using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class BasinOutputViewDetachedTests
{
    private static void Pump(BasinCompositorHost? host)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        host?.Loop.Dispatch(0);
    }

    [AvaloniaFact]
    public void A_detached_view_starts_its_host_and_runs_posted_work_without_a_shown_window()
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        BasinCounters.Reset();
        BasinCompositorHost? created = null;
        BasinCompositorHost? ready = null;
        var view = new BasinOutputView(
            () => created = new BasinCompositorHost(new BasinCompositorOptions { AppName = "basin-tests", ConfigureServices = HostedDrivers.Without }),
            createOwnView: false);
        view.HostReady += host => ready = host;
        var window = new Window { Width = 1, Height = 1 };
        var compositor = ElementComposition.GetElementVisual(window)?.Compositor;
        Assert.NotNull(compositor);

        view.StartDetached(compositor);
        for (var i = 0; i < 50 && ready is null; i++)
        {
            Pump(created);
        }

        Assert.False(window.IsVisible);
        Assert.NotNull(ready);
        Assert.Same(created, ready);

        var ran = false;
        view.Post(() => ran = true);
        for (var i = 0; i < 50 && !ran; i++)
        {
            Pump(created);
        }

        Assert.True(ran, "posted work never ran on a detached view");

        var shutdown = view.ShutdownAsync();
        for (var i = 0; i < 50 && !shutdown.IsCompleted; i++)
        {
            Pump(null);
        }

        Assert.True(shutdown.IsCompletedSuccessfully, "the detached host never shut down");
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_detached_view_refuses_to_create_its_own_view_output()
    {
        var view = new BasinOutputView(() => throw new InvalidOperationException("never created"));
        var window = new Window();
        var compositor = ElementComposition.GetElementVisual(window)!.Compositor;

        Assert.Throws<InvalidOperationException>(() => view.StartDetached(compositor));
        window.Close();
    }
}
