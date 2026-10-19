using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Shell.Nested;
using Basin.Tests.Nested;
using Xunit;

namespace Basin.Tests;

public sealed class HeadlessDriverTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    private sealed class Rig : IDisposable
    {
        private NestedShell? _shell;
        private BasinViewOutput? _view;

        public Rig()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            int serverFd, clientFd;
            unsafe
            {
                var fds = stackalloc int[2];
                Assert.Equal(0, socketpair(1, 1, 0, fds));
                serverFd = fds[0];
                clientFd = fds[1];
            }

            Driver = new BasinHeadlessDriver(() =>
            {
                var host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "basin-tests", ConfigureServices = HostedDrivers.Without });
                _view = host.CreateViewOutput(640, 480, 1.0, NestedShell.OutputKey);
                _shell = new NestedShell(
                    host, _view, new ShellSettings(), new PanelLayout(0, 0, 0), KeyTable.Empty, Post, [TestTheme.Load]);
                host.Display.CreateClient(serverFd);
                return host;
            });
            Driver.Start();
            Client = new ShmTestClient(clientFd);
            Client.BindGlobals(() => Client.Display.Roundtrip());
        }

        public BasinHeadlessDriver Driver { get; }

        public ShmTestClient Client { get; }

        public NestedShell Shell => _shell!;

        private void Post(Action action) => Driver.Post(action);

        public void Until(Func<bool> settled, string what)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!settled() && Environment.TickCount64 < deadline)
            {
                Client.Display.Roundtrip();
                Thread.Sleep(2);
            }

            Assert.True(settled(), what);
        }

        public void Dispose()
        {
            Client.Dispose();
            Driver.Stop(() =>
            {
                _shell?.Dispose();
                _view?.Dispose();
            });
            LeakTracking.Expect(0, BasinCounters.LiveObjects);
        }
    }

    [Fact]
    public void A_headless_run_sends_frame_callbacks_and_presents_feedback_without_rendering()
    {
        using var rig = new Rig();
        var client = rig.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        toplevel.SetAppId("org.basin.headless");
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };
        surface.Commit();
        rig.Until(() => configured, "the headless compositor never configured the toplevel");

        var presented = 0;
        var discarded = 0;
        var done = 0;
        for (var i = 0; i < 3; i++)
        {
            var buffer = client.CreateBuffer(120, 90, NestedShellHarness.Fill(120, 90, 0xFF3366AA));
            var feedback = client.Presentation!.Feedback(surface);
            feedback.Presented += (_, _) => presented++;
            feedback.Discarded += (_, _) => discarded++;
            var callback = surface.Frame();
            callback.Done += (_, _) => done++;
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 120, 90);
            surface.Commit();
            var expected = i + 1;
            rig.Until(() => done >= expected, "the headless driver never sent the frame callback");
            rig.Until(() => presented + discarded >= expected, "the presentation feedback was never answered");
        }

        Assert.Equal(3, presented);
        Assert.True(rig.Driver.Frames >= 3);

        var idle = rig.Driver.Frames;
        Thread.Sleep(150);
        client.Display.Roundtrip();
        Assert.InRange(rig.Driver.Frames, idle, idle + 1);

        toplevel.Dispose();
        xdgSurface.Dispose();
        surface.Dispose();
    }

    [Fact]
    public async Task A_recording_reads_frames_the_headless_driver_renders_for_it()
    {
        using var rig = new Rig();
        var (toplevel, xdgSurface, surface) = MapColored(rig, 0xFF3366AA);

        var done = new TaskCompletionSource<RecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refused = await rig.Driver.InvokeAsync(() => rig.Driver.Host.Services.Require<ISceneDiagnostics>().Record(
            new RecordRequest(string.Empty, 3) { Region = new Box(0, 0, 640, 480), KeepPixels = true, TimeoutMs = 5000 },
            done.SetResult));
        Assert.Null(refused);
        var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal(3, result.Frames);
        Assert.True(result.LumaHigh > 0);
        Assert.True(Count(result.Pixels!, 0x33, 0x66, 0xAA) >= 120 * 90 * 3);

        toplevel.Dispose();
        xdgSurface.Dispose();
        surface.Dispose();
    }

    [Fact]
    public async Task A_screenshot_request_is_written_by_the_headless_driver()
    {
        using var rig = new Rig();
        var (toplevel, xdgSurface, surface) = MapColored(rig, 0xFF3366AA);
        var path = Path.Combine(Path.GetTempPath(), $"basin-headless-{Environment.ProcessId}-{Guid.NewGuid():N}.png");
        try
        {
            var written = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Driver.Post(() => rig.Driver.Host.RequestScreenshot(path, written.SetResult));

            Assert.True(await written.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            var png = File.ReadAllBytes(path);
            Assert.True(png.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']));
        }
        finally
        {
            File.Delete(path);
            toplevel.Dispose();
            xdgSurface.Dispose();
            surface.Dispose();
        }
    }

    private static (Basin.Shell.Xdg.Protocol.XdgToplevel, Basin.Shell.Xdg.Protocol.XdgSurface, Wayland.WlSurface) MapColored(Rig rig, uint argb)
    {
        var client = rig.Client;
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        var configured = false;
        xdgSurface.Configure += (_, e) =>
        {
            xdgSurface.AckConfigure(e.Serial);
            configured = true;
        };
        surface.Commit();
        rig.Until(() => configured, "the headless compositor never configured the toplevel");

        var buffer = client.CreateBuffer(120, 90, NestedShellHarness.Fill(120, 90, argb));
        var frame = false;
        surface.Frame().Done += (_, _) => frame = true;
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 120, 90);
        surface.Commit();
        rig.Until(() => frame, "the headless driver never framed the mapped toplevel");
        return (toplevel, xdgSurface, surface);
    }

    private static int Count(byte[] rgba, byte r, byte g, byte b)
    {
        var count = 0;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i] == r && rgba[i + 1] == g && rgba[i + 2] == b)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task Posted_work_runs_on_the_driver_thread()
    {
        using var rig = new Rig();
        Assert.True(await rig.Driver.InvokeAsync(() => rig.Driver.IsDriverThread));
        Assert.False(rig.Driver.IsDriverThread);
        Assert.Equal(0, await rig.Driver.InvokeAsync(() => rig.Shell.Windows.Count));
    }
}
