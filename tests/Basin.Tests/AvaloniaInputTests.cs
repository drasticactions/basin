using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaInputTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc")]
    private static extern unsafe int poll(PollFd* fds, nuint count, int timeoutMs);

    [DllImport("libc")]
    private static extern nint mmap(nint addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc")]
    private static extern int munmap(nint addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

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
            Host = new BasinCompositorHost(new BasinCompositorOptions { AppName = "waylonia-tests" });
            Manager = new ToplevelWindows(Host, action => action());

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

        public void PumpUntil(Func<bool> condition, int rounds = 50)
        {
            for (var i = 0; i < rounds && !condition(); i++)
            {
                Pump();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(condition(), "condition not reached while pumping");
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
            Host.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void The_host_keymap_source_still_delivers_shift_to_the_client()
    {
        var harness = new Harness();
        var source = new Basin.Seat.HostKeymapSource();
        harness.Host.Seat.Keyboard.KeymapSource = source;
        harness.Host.Seat.Keyboard.SetKeymapFromHost();
        var client = harness.Client;

        var keyboardProxy = client.Seat!.GetKeyboard();
        var keyboardEntered = false;
        var gotKeymap = false;
        uint depressed = 0;
        keyboardProxy.Enter += (_, _) => keyboardEntered = true;
        keyboardProxy.Keymap += (_, e) =>
        {
            gotKeymap = true;
            close(e.Fd);
        };
        keyboardProxy.Modifiers += (_, e) => depressed = e.ModsDepressed;

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        uint serial = 0;
        xdgSurface.Configure += (_, e) => serial = e.Serial;
        surface.Commit();
        harness.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);
        var buffer = client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 150);
        surface.Commit();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 1);

        var window = harness.Manager.Windows.First();
        window.Activate();
        harness.PumpUntil(() => keyboardEntered);
        Assert.True(gotKeymap, "no wl_keyboard.keymap arrived from the host source");

        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        harness.PumpUntil(() => (depressed & 1) != 0);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);
        harness.PumpUntil(() => (depressed & 1) == 0);

        keyboardProxy.Release();
        toplevelProxy.Destroy();
        xdgSurface.Destroy();
        surface.Destroy();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 0);
        harness.Dispose();
        source.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }

    [AvaloniaFact]
    public void A_scripted_host_sequence_reaches_the_client_with_the_host_keymap()
    {
        var harness = new Harness();
        harness.Host.Seat.Keyboard.SetKeymap(new Capabilities.KeymapNames(null, null, "de", null, null));
        var client = harness.Client;

        var pointerProxy = client.Seat!.GetPointer();
        var keyboardProxy = client.Seat.GetKeyboard();
        var pointerEntered = false;
        double motionX = 0, motionY = 0;
        var buttons = new List<(uint Button, bool Pressed)>();
        var axes = new List<double>();
        var keys = new List<(uint Key, bool Pressed)>();
        var keyboardEntered = false;
        string keymapText = "";
        pointerProxy.Enter += (_, _) => pointerEntered = true;
        pointerProxy.Motion += (_, e) => (motionX, motionY) = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        pointerProxy.Button += (_, e) => buttons.Add((e.Button, e.State == WlPointer.ButtonState.Pressed));
        pointerProxy.AxisEvent += (_, e) => axes.Add(e.Value.ToDouble());
        keyboardProxy.Enter += (_, _) => keyboardEntered = true;
        keyboardProxy.Key += (_, e) => keys.Add((e.Key, e.State == WlKeyboard.KeyState.Pressed));
        uint depressed = 0;
        keyboardProxy.Modifiers += (_, e) => depressed = e.ModsDepressed;
        keyboardProxy.Keymap += (_, e) =>
        {
            var data = mmap(0, e.Size, 1, 2, e.Fd, 0);
            if (data != -1)
            {
                keymapText = Marshal.PtrToStringUTF8(data, (int)e.Size);
                munmap(data, e.Size);
            }

            close(e.Fd);
        };

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        uint serial = 0;
        xdgSurface.Configure += (_, e) => serial = e.Serial;
        surface.Commit();
        harness.PumpUntil(() => serial != 0);
        xdgSurface.AckConfigure(serial);
        var buffer = client.CreateBuffer(200, 150, Fill.Solid(200, 150, 0xFF336699));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 200, 150);
        surface.Commit();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 1);

        var window = harness.Manager.Windows.First();
        window.Activate();
        harness.PumpUntil(() => keyboardEntered);
        using (var keymapContext = Xkb.XkbContext.Create())
        using (var hostKeymap = keymapContext.CreateKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(keymapText)))
        {
            Assert.NotNull(hostKeymap);
            using var keymapState = hostKeymap!.CreateState();
            Assert.Equal("z", keymapState.GetKeyString(21 + 8));
        }

        window.MouseMove(new global::Avalonia.Point(30, 20));
        harness.PumpUntil(() => pointerEntered);
        harness.PumpUntil(() => motionX > 29 && motionY > 19);

        window.MouseDown(new global::Avalonia.Point(30, 20), MouseButton.Left);
        window.MouseUp(new global::Avalonia.Point(30, 20), MouseButton.Left);
        harness.PumpUntil(() => buttons.Count == 2);
        Assert.Equal((0x110u, true), buttons[0]);
        Assert.Equal((0x110u, false), buttons[1]);

        window.MouseWheel(new global::Avalonia.Point(30, 20), new Vector(0, -1));
        harness.PumpUntil(() => axes.Count > 0);
        Assert.True(axes[0] > 0, $"scroll down should be a positive axis value, got {axes[0]}");

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);
        harness.PumpUntil(() => keys.Count == 2);
        Assert.Equal((30u, true), keys[0]);
        Assert.Equal((30u, false), keys[1]);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        harness.PumpUntil(() => keys.Count == 4);
        Assert.Equal((15u, true), keys[2]);
        Assert.Equal((15u, false), keys[3]);

        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        harness.PumpUntil(() => keys.Count == 6);
        Assert.Equal((42u, true), keys[4]);
        Assert.Equal((15u, true), keys[5]);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        harness.PumpUntil(() => keys.Count == 7);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Shift);
        harness.PumpUntil(() => keys.Count == 8);
        Assert.Equal((30u, true), keys[7]);
        harness.PumpUntil(() => (depressed & 1) != 0);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);
        harness.PumpUntil(() => keys.Count == 10);
        harness.PumpUntil(() => (depressed & 1) == 0);

        pointerProxy.Release();
        keyboardProxy.Release();
        toplevelProxy.Destroy();
        xdgSurface.Destroy();
        surface.Destroy();
        harness.PumpUntil(() => harness.Manager.Windows.Count == 0);
        harness.Dispose();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }
}
