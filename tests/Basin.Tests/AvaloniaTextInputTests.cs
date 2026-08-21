using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaTextInputTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc")]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            CompositorTestHost.SkipWithoutWaylandClient();
            BasinCounters.Reset();
            TextInput = new AvaloniaTextInput(action => action());
            Host = new BasinCompositorHost(new BasinCompositorOptions
            {
                AppName = "waylonia-tests",
                TextInput = TextInput,
            });
            Manager = new ToplevelWindows(Host, action => action());
            Manager.AttachTextInput(TextInput);

            int serverFd, clientFd;
            unsafe
            {
                var fds = stackalloc int[2];
                Assert.Equal(0, socketpair(1, 1, 0, fds));
                serverFd = fds[0];
                clientFd = fds[1];
            }

            Host.Display.CreateClient(serverFd);
            Client = new ShmTestClient(clientFd);
            Client.BindGlobals(Pump);
        }

        public AvaloniaTextInput TextInput { get; }

        public BasinCompositorHost Host { get; }

        public ToplevelWindows Manager { get; }

        public ShmTestClient Client { get; }

        public void Pump()
        {
            Client.Display.Flush();
            Host.Session.BeginFrame();
            Host.Session.EndFrame();
            while (Readable())
            {
                Client.Display.Dispatch();
            }

            Client.Display.DispatchPending();
            Dispatcher.UIThread.RunJobs();
        }

        public void PumpUntil(Func<bool> condition, int rounds = 100)
        {
            for (var i = 0; i < rounds && !condition(); i++)
            {
                Pump();
                Thread.Sleep(5);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(condition(), "condition not reached while pumping");
        }

        public Surface MapToplevel()
        {
            var surface = Client.Compositor.CreateSurface();
            var xdgSurface = Client.WmBase!.GetXdgSurface(surface);
            _ = xdgSurface.GetToplevel();
            uint serial = 0;
            xdgSurface.Configure += (_, e) => serial = e.Serial;
            surface.Commit();
            PumpUntil(() => serial != 0);
            xdgSurface.AckConfigure(serial);
            var buffer = Client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
            surface.Attach(buffer.Proxy, 0, 0);
            surface.Damage(0, 0, 200, 150);
            surface.Commit();
            PumpUntil(() => Manager.Windows.Count == 1);
            return System.Linq.Enumerable.First(Host.Services.Require<CompositorGlobal>().Surfaces);
        }

        private bool Readable()
        {
            unsafe
            {
                var pollFd = new PollFd { Fd = Client.Display.Fd, Events = 1 };
                return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Host.Loop.Dispatch(0);
            Host.Loop.Dispatch(0);
            Dispatcher.UIThread.RunJobs();
            Manager.Dispose();
            TextInput.Dispose();
            Host.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void A_scripted_composition_produces_the_text_input_v3_sequence()
    {
        var harness = new Harness();
        var client = harness.Client;
        _ = harness.MapToplevel();
        var window = harness.Manager.Windows.First();
        window.Activate();
        harness.Pump();

        var manager = client.Registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(
            client.Globals.First(g => g.Interface == "zwp_text_input_manager_v3").Name, 1);
        var textInput = manager.GetTextInput(client.Seat!);
        var entered = false;
        var preedits = new List<(string Text, int Begin, int End)>();
        var commits = new List<string>();
        var dones = 0;
        textInput.Enter += (_, _) => entered = true;
        textInput.PreeditString += (_, e) => preedits.Add((e.Text ?? string.Empty, e.CursorBegin, e.CursorEnd));
        textInput.CommitString += (_, e) => commits.Add(e.Text ?? string.Empty);
        textInput.Done += (_, _) => dones++;
        harness.PumpUntil(() => entered);

        textInput.Enable();
        textInput.SetCursorRectangle(12, 34, 2, 18);
        textInput.Commit();
        harness.PumpUntil(() => harness.TextInput.ActiveWindow is not null);
        Assert.Same(window, harness.TextInput.ActiveWindow);
        Assert.Equal(new global::Avalonia.Rect(12, 34, 2, 18), harness.TextInput.Client.CursorRectangle);

        harness.TextInput.Client.SetPreeditText("にほん", 3);
        harness.PumpUntil(() => preedits.Count > 0);
        Assert.Equal("にほん", preedits[^1].Text);
        Assert.Equal(9, preedits[^1].Begin);
        Assert.Equal(9, preedits[^1].End);
        Assert.True(dones > 0);

        window.KeyTextInput("日本");
        harness.PumpUntil(() => commits.Count > 0);
        Assert.Equal("日本", commits[^1]);
        Assert.Equal(string.Empty, preedits[^1].Text);

        textInput.Disable();
        textInput.Commit();
        harness.PumpUntil(() => harness.TextInput.ActiveWindow is null);

        var commitsBefore = commits.Count;
        window.KeyTextInput("plain");
        for (var i = 0; i < 10; i++)
        {
            harness.Pump();
        }

        Assert.Equal(commitsBefore, commits.Count);

        textInput.Dispose();
        manager.Dispose();
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }
}
