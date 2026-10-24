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

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [AvaloniaFact]
    public void A_detached_host_given_the_toolkit_EGL_display_offers_dmabuf_to_its_first_client()
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        Assert.SkipUnless(File.Exists(CompositorTestHost.RenderNodePath), "no render node");
        BasinCounters.Reset();
        var gpu = HostedGpuContext.Create(CompositorTestHost.RenderNodePath);
        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            Assert.Equal(0, socketpair(1, 1, 0, fds));
            serverFd = fds[0];
            clientFd = fds[1];
        }

        BasinCompositorHost? created = null;
        var window = new Window { Width = 1, Height = 1 };
        var compositor = ElementComposition.GetElementVisual(window)!.Compositor;
        var visual = compositor.CreateCustomVisual(new BasinOutputVisual(
            () =>
            {
                created = new BasinCompositorHost(new BasinCompositorOptions
                {
                    AppName = "basin-tests",
                    DmabufRequiresGpuCapture = true,
                    ConfigureServices = HostedDrivers.Without,
                });
                created.Display.CreateClient(serverFd);
                return created;
            },
            createOwnView: false));
        visual.SendHandlerMessage(new BasinEglDisplayMessage(gpu.EglDisplayHandle));
        visual.SendHandlerMessage(BasinOutputVisual.StartMessage);
        for (var i = 0; i < 50 && created is null; i++)
        {
            Pump(null);
        }

        Assert.NotNull(created);
        Assert.NotNull(created.Dmabuf);
        using var client = new ShmTestClient(clientFd);
        client.BindGlobals(() =>
        {
            client.Display.Flush();
            created.Loop.Dispatch(0);
            created.Display.FlushClients();
            client.Display.Dispatch();
        });
        Assert.NotNull(client.Dmabuf);

        var message = new BasinShutdownMessage();
        visual.SendHandlerMessage(message);
        for (var i = 0; i < 50 && !message.Completed.IsCompleted; i++)
        {
            Pump(null);
        }

        Assert.True(message.Completed.IsCompletedSuccessfully);
        gpu.Dispose();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
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
