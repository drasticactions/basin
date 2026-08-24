using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class KeyStateTests
{
    private const uint Capslock = 0;
    private const uint Numlock = 1;
    private const uint Scrolllock = 2;
    private const uint Shift = 5;

    private const uint Unlocked = 0;
    private const uint Latched = 1;
    private const uint Locked = 2;
    private const uint Pressed = 3;

    private const uint CapsKey = 58;
    private const uint ShiftKey = 42;
    private const uint ScrollKey = 70;

    [Fact]
    public void Fetch_states_sends_eight_events_at_version_5_and_three_at_version_4()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);

        var v5Events = new List<(uint Key, uint State)>();
        var v4Events = new List<(uint Key, uint State)>();
        var v5 = Bind(host, 5, v5Events);
        var v4 = Bind(host, 4, v4Events);

        v5.FetchStates();
        v4.FetchStates();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(8, v5Events.Count);
        Assert.Equal(3, v4Events.Count);
        Assert.Equal([0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u], v5Events.Select(e => e.Key).Order());
        Assert.Equal([Capslock, Numlock, Scrolllock], v4Events.Select(e => e.Key).Order());
        Assert.All(v5Events, e => Assert.Equal(Unlocked, e.State));
        Assert.All(v4Events, e => Assert.Equal(Unlocked, e.State));
    }

    [Fact]
    public void Pressed_reaches_a_version_5_resource_and_never_a_version_4_one()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);
        host.Seat.Keyboard.NotifyKey(10, ShiftKey, WlKeyboard.KeyState.Pressed);

        var v5Events = new List<(uint Key, uint State)>();
        var v4Events = new List<(uint Key, uint State)>();
        var v5 = Bind(host, 5, v5Events);
        var v4 = Bind(host, 4, v4Events);

        v5.FetchStates();
        v4.FetchStates();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Contains((Shift, Pressed), v5Events);
        Assert.Equal(3, v4Events.Count);
        Assert.All(v4Events, e => Assert.Equal(Unlocked, e.State));
    }

    [Fact]
    public void A_modifier_both_locked_and_depressed_reports_locked()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);
        host.Seat.Keyboard.NotifyKey(10, CapsKey, WlKeyboard.KeyState.Pressed);

        var events = new List<(uint Key, uint State)>();
        var proxy = Bind(host, 5, events);

        proxy.FetchStates();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Contains((Capslock, Locked), events);
        Assert.DoesNotContain((Capslock, Pressed), events);
    }

    [Fact]
    public void Locking_caps_sends_a_burst_to_every_resource_without_a_request()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);

        var v5Events = new List<(uint Key, uint State)>();
        var v4Events = new List<(uint Key, uint State)>();
        _ = Bind(host, 5, v5Events);
        _ = Bind(host, 4, v4Events);

        host.Seat.Keyboard.NotifyKey(10, CapsKey, WlKeyboard.KeyState.Pressed);
        host.PumpToClient();

        Assert.Equal(8, v5Events.Count);
        Assert.Equal(3, v4Events.Count);
        Assert.Contains((Capslock, Locked), v5Events);
        Assert.Contains((Capslock, Locked), v4Events);
    }

    [Fact]
    public void Scroll_lock_follows_the_led_and_never_latches_or_presses()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(TestKeymaps.ScrollLock));
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);

        var events = new List<(uint Key, uint State)>();
        var proxy = Bind(host, 5, events);

        host.Seat.Keyboard.NotifyKey(10, ScrollKey, WlKeyboard.KeyState.Pressed);
        host.PumpToClient();
        Assert.Contains((Scrolllock, Locked), events);

        host.Seat.Keyboard.NotifyKey(20, ScrollKey, WlKeyboard.KeyState.Released);
        events.Clear();
        proxy.FetchStates();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Contains((Scrolllock, Locked), events);

        host.Seat.Keyboard.NotifyKey(30, ScrollKey, WlKeyboard.KeyState.Pressed);
        host.Seat.Keyboard.NotifyKey(40, ScrollKey, WlKeyboard.KeyState.Released);
        events.Clear();
        proxy.FetchStates();
        host.PumpToServer();
        host.PumpToClient();
        Assert.Contains((Scrolllock, Unlocked), events);

        Assert.DoesNotContain(events, e => e.Key == Scrolllock && (e.State == Latched || e.State == Pressed));
    }

    [Fact]
    public void A_keymap_change_resends_to_every_resource()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);

        var events = new List<(uint Key, uint State)>();
        _ = Bind(host, 5, events);

        host.Seat.Keyboard.SetKeymapFromBuffer(System.Text.Encoding.UTF8.GetBytes(TestKeymaps.ScrollLock));
        host.PumpToClient();

        Assert.Equal([0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u], events.Select(e => e.Key).Distinct().Order());
    }

    [Fact]
    public void Two_clients_at_different_versions_each_get_their_own_burst()
    {
        using var host = new CompositorTestHost();
        host.Seat.Keyboard.SetKeymap();
        using var manager = new Basin.Plasma.KeyStateManager(host.Display, host.Seat);
        var other = host.ConnectClient();

        var v5Events = new List<(uint Key, uint State)>();
        var v4Events = new List<(uint Key, uint State)>();
        _ = Bind(host, 5, v5Events);
        _ = Bind(host, 4, v4Events, other);

        host.Seat.Keyboard.NotifyKey(10, ShiftKey, WlKeyboard.KeyState.Pressed);
        host.PumpToClient();

        Assert.Equal(8, v5Events.Count);
        Assert.Equal(3, v4Events.Count);
        Assert.Contains((Shift, Pressed), v5Events);
        Assert.All(v4Events, e => Assert.Equal(Unlocked, e.State));
    }

    private static Basin.Plasma.Protocol.OrgKdeKwinKeystate Bind(
        CompositorTestHost host, uint version, List<(uint Key, uint State)> events, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.OrgKdeKwinKeystate? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_keystate")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.OrgKdeKwinKeystate>(e.Name, version);
                proxy.StateChanged += (_, se) => events.Add((se.Key, se.State));
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        host.PumpToServer();
        return proxy!;
    }
}
