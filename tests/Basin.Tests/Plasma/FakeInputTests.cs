using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using Wayland;
using Xkb;
using Xunit;

namespace Basin.Tests;

public sealed class FakeInputTests
{
    private const uint BtnLeft = 0x110;
    private const uint KeyA = 30;
    private const uint KeysymF = 0x0046;
    private const uint KeysymShiftL = 0xffe1;
    private const uint KeysymEdiaeresis = 0x00eb;
    private const uint UnmappedKeyCode = 247;

    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int bind(int fd, byte* addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int connect(int fd, byte* addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    private static extern uint getgid();

    private sealed class RecordingSink : IInputSink
    {
        public List<string> Events { get; } = [];

        public IInjectedKeyboard? CreateKeyboard() => null;

        public bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
        {
            Events.Add($"key {keycode} {(pressed ? "down" : "up")}");
            return true;
        }

        public bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group)
        {
            Events.Add($"mods {depressed}");
            return true;
        }

        public bool PointerMotion(uint timeMs, double dx, double dy)
        {
            Events.Add($"motion {dx} {dy}");
            return true;
        }

        public bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
        {
            Events.Add($"absolute {x} {y} {width} {height}");
            return true;
        }

        public bool PointerButton(uint timeMs, uint button, bool pressed)
        {
            Events.Add($"button {button} {(pressed ? "down" : "up")}");
            return true;
        }

        public bool PointerAxis(uint timeMs, uint axis, double value)
        {
            Events.Add($"axis {axis} {value}");
            return true;
        }

        public bool PointerAxisSource(uint source) => true;

        public bool PointerAxisStop(uint timeMs, uint axis) => true;

        public bool Frame() => true;

        public bool TouchDown(uint timeMs, int id, double x, double y, double width, double height)
        {
            Events.Add($"touchdown {id} {x} {y} {width} {height}");
            return true;
        }

        public bool TouchMotion(uint timeMs, int id, double x, double y, double width, double height)
        {
            Events.Add($"touchmotion {id} {x} {y} {width} {height}");
            return true;
        }

        public bool TouchUp(uint timeMs, int id)
        {
            Events.Add($"touchup {id}");
            return true;
        }

        public bool TouchFrame()
        {
            Events.Add("touchframe");
            return true;
        }

        public bool TouchCancel()
        {
            Events.Add("touchcancel");
            return true;
        }
    }

    private sealed class RecordingAuthority(params bool[] answers) : IFakeInputAuthority
    {
        private int _next;

        public List<FakeInputRequest> Requests { get; } = [];

        public List<object> RevokedClients { get; } = [];

        public bool Authorize(in FakeInputRequest request)
        {
            Requests.Add(request);
            var answer = answers.Length == 0 || answers[Math.Min(_next, answers.Length - 1)];
            _next++;
            return answer;
        }

        public void Revoked(object client) => RevokedClients.Add(client);
    }

    private static Basin.Plasma.Protocol.OrgKdeKwinFakeInput Bind(
        CompositorTestHost host, ShmTestClient? client = null, uint version = 6)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinFakeInput? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_fake_input")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinFakeInput>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        host.PumpToServer();
        return proxy!;
    }

    [Fact]
    public void Injection_before_authenticate_reaches_nothing()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.PointerMotion(WlFixed.FromDouble(5), WlFixed.FromDouble(5));
        proxy.Button(BtnLeft, 1);
        proxy.KeyboardKey(KeyA, 1);
        proxy.TouchDown(0, WlFixed.FromDouble(0.5), WlFixed.FromDouble(0.5));
        host.PumpToServer();

        Assert.Empty(sink.Events);
        Assert.Empty(authority.Requests);
    }

    [Fact]
    public void A_refusing_authority_keeps_the_device_inert()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(false);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.PointerMotion(WlFixed.FromDouble(5), WlFixed.FromDouble(5));
        host.PumpToServer();

        Assert.Single(authority.Requests);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void An_accepting_authority_sees_the_request_and_injection_reaches_the_sink()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("kdeconnect", "remote input");
        proxy.PointerMotion(WlFixed.FromDouble(3), WlFixed.FromDouble(4));
        host.PumpToServer();

        var request = Assert.Single(authority.Requests);
        Assert.Equal("kdeconnect", request.Application);
        Assert.Equal("remote input", request.Reason);
        Assert.Equal((uint)Environment.ProcessId, request.Pid);
        Assert.Equal(getuid(), request.Uid);
        Assert.Equal(getgid(), request.Gid);
        Assert.Null(request.SandboxAppId);
        Assert.Null(request.SandboxEngine);
        Assert.Equal(["motion 3 4"], sink.Events);
    }

    [Fact]
    public unsafe void A_sandboxed_client_carries_its_security_context_identity()
    {
        using var host = new CompositorTestHost();
        using var contexts = new Basin.Desktop.SecurityContextManager(host.Display, host.Loop);
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        Basin.Desktop.Protocol.WpSecurityContextManagerV1? contextManager = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_security_context_manager_v1")
            {
                contextManager = registry.Bind<Basin.Desktop.Protocol.WpSecurityContextManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(contextManager);

        var path = Path.Combine(Path.GetTempPath(), $"basin-fakeinput-secctx-{Environment.ProcessId}");
        File.Delete(path);
        var listenFd = socket(1, 1, 0);
        Assert.True(listenFd >= 0);
        var addr = new byte[110];
        addr[0] = 1;
        Encoding.UTF8.GetBytes(path).CopyTo(addr, 2);
        fixed (byte* p = addr)
        {
            Assert.Equal(0, bind(listenFd, p, (uint)addr.Length));
        }

        Assert.Equal(0, listen(listenFd, 4));
        int closeRead, closeWrite;
        var fds = stackalloc int[2];
        Assert.Equal(0, pipe(fds));
        closeRead = fds[0];
        closeWrite = fds[1];

        var context = contextManager!.CreateListener(listenFd, closeRead);
        close(listenFd);
        close(closeRead);
        context.SetSandboxEngine("org.flatpak");
        context.SetAppId("dev.example.Sandboxed");
        context.Commit();
        host.PumpToServer();

        var appFd = socket(1, 1, 0);
        fixed (byte* p = addr)
        {
            Assert.Equal(0, connect(appFd, p, (uint)addr.Length));
        }

        var connected = false;
        contexts.ClientConnected += (_, _) => connected = true;
        host.PumpUntil(() => connected);

        var sandboxed = host.AdoptClient(appFd);
        var proxy = Bind(host, sandboxed);
        proxy.Authenticate("sandboxed", "testing");
        sandboxed.Display.Flush();
        host.PumpToServer();

        var request = Assert.Single(authority.Requests);
        Assert.Equal("dev.example.Sandboxed", request.SandboxAppId);
        Assert.Equal("org.flatpak", request.SandboxEngine);

        close(closeWrite);
        File.Delete(path);
    }

    [Fact]
    public void Two_binds_are_two_devices_authorised_independently()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true, false);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var first = Bind(host);
        var second = Bind(host);
        first.Authenticate("first", "yes");
        second.Authenticate("second", "no");
        host.PumpToServer();
        Assert.Equal(2, authority.Requests.Count);

        first.PointerMotion(WlFixed.FromDouble(1), WlFixed.FromDouble(0));
        second.PointerMotion(WlFixed.FromDouble(2), WlFixed.FromDouble(0));
        host.PumpToServer();

        Assert.Equal(["motion 1 0"], sink.Events);
    }

    [Fact]
    public void Destroying_a_device_releases_its_buttons_and_keys()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.Button(BtnLeft, 1);
        proxy.KeyboardKey(KeyA, 1);
        host.PumpToServer();
        sink.Events.Clear();

        proxy.Destroy();
        host.PumpToServer();

        Assert.Contains($"button {BtnLeft} up", sink.Events);
        Assert.Contains($"key {KeyA} up", sink.Events);
    }

    [Fact]
    public void Destroying_a_device_cancels_a_live_touch_sequence()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.TouchDown(0, WlFixed.FromDouble(0.25), WlFixed.FromDouble(0.25));
        host.PumpToServer();
        sink.Events.Clear();

        proxy.Destroy();
        host.PumpToServer();

        Assert.Equal(["touchcancel"], sink.Events);
    }

    [Fact]
    public void A_double_press_injects_once_and_an_unmatched_release_injects_nothing()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.KeyboardKey(KeyA, 1);
        proxy.KeyboardKey(KeyA, 1);
        proxy.KeyboardKey(31, 0);
        proxy.Button(BtnLeft, 0);
        host.PumpToServer();

        Assert.Equal([$"key {KeyA} down"], sink.Events);
    }

    [Fact]
    public void Absolute_motion_is_layout_coordinates_and_touch_is_a_fraction_of_the_first_output()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.PointerMotionAbsolute(WlFixed.FromDouble(50), WlFixed.FromDouble(60));
        proxy.TouchDown(0, WlFixed.FromDouble(0.5), WlFixed.FromDouble(0.5));
        proxy.TouchFrame();
        host.PumpToServer();

        Assert.Equal(
            ["absolute 50 60 160 120", "touchdown 0 80 60 160 120", "touchframe"],
            sink.Events);
    }

    [Fact]
    public void A_keysym_on_the_keymap_presses_its_keycode_and_restores_the_modifier_mask()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.KeyboardKeysym(KeysymF, 1);
        proxy.KeyboardKeysym(KeysymF, 0);
        host.PumpToServer();

        Assert.Equal(6, sink.Events.Count);
        Assert.StartsWith("mods ", sink.Events[0]);
        Assert.NotEqual("mods 0", sink.Events[0]);
        Assert.StartsWith("key 33 down", sink.Events[1]);
        Assert.Equal("mods 0", sink.Events[2]);
        Assert.Equal("key 33 up", sink.Events[4]);
        Assert.Equal("mods 0", sink.Events[5]);
    }

    [Fact]
    public void A_modifier_keysym_stays_held()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.KeyboardKeysym(KeysymShiftL, 1);
        host.PumpToServer();

        Assert.Equal(["key 42 down"], sink.Events);
    }

    [Fact]
    public void A_keysym_off_the_keymap_swaps_injects_releases_and_restores()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        var before = host.Seat.Keyboard.Keymap!.AsString(XkbKeymapFormat.TextV1);
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        proxy.KeyboardKeysym(KeysymEdiaeresis, 1);
        proxy.KeyboardKeysym(KeysymEdiaeresis, 0);
        host.PumpToServer();

        Assert.Equal([$"key {UnmappedKeyCode} down", $"key {UnmappedKeyCode} up"], sink.Events);
        Assert.Equal(before, host.Seat.Keyboard.Keymap!.AsString(XkbKeymapFormat.TextV1));
    }

    [Fact]
    public void Revoked_fires_once_per_device_teardown()
    {
        using var host = new CompositorTestHost();
        var sink = new RecordingSink();
        var authority = new RecordingAuthority(true);
        using var manager = new Basin.Plasma.FakeInputManager(host.Display, authority, sink, host.Seat, host.Layout);

        var proxy = Bind(host);
        proxy.Authenticate("test", "testing");
        host.PumpToServer();
        Assert.Empty(authority.RevokedClients);

        proxy.Destroy();
        host.PumpToServer();

        Assert.Single(authority.RevokedClients);
    }
}
