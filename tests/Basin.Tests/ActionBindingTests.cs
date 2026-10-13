using Basin.Capabilities;
using Basin.Config;
using Basin.Hypr;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class ActionBindingTests
{
    private const uint KeyA = 30;
    private const uint SuperL = 0xffeb;
    private const uint LowerA = 0x61;

    [Fact]
    public void A_no_block_shortcut_fires_and_leaves_both_key_events_to_the_client()
    {
        var registry = new PreferredTriggerShortcuts();
        var observer = new RecordingObserver();
        registry.AddObserver(observer);
        Assert.True(registry.TryRegister(new GlobalShortcutInfo("app", "shot", "", "", "LOGO+a", GlobalShortcutFlags.NoBlock)));

        Assert.False(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: true));
        Assert.False(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: false));
        Assert.Equal(["on app:shot", "off app:shot"], observer.Log);
    }

    [Fact]
    public void A_one_shot_shortcut_fires_once_and_is_gone_after_its_release()
    {
        var registry = new PreferredTriggerShortcuts();
        var observer = new RecordingObserver();
        registry.AddObserver(observer);
        Assert.True(registry.TryRegister(new GlobalShortcutInfo("app", "once", "", "", "LOGO+a", GlobalShortcutFlags.OneShot)));

        Assert.True(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: true));
        Assert.True(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: false));
        Assert.False(registry.IsRegistered("app", "once"));
        Assert.False(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: true));
        Assert.Equal(["on app:once", "off app:once", "removed app:once"], observer.Log);
    }

    [Fact]
    public void Overrides_from_the_compositor_beat_the_preferred_trigger()
    {
        var registry = new PreferredTriggerShortcuts
        {
            Overrides = new Dictionary<(string, string), (uint, Modifiers)> { [("app", "shot")] = (0x62, Modifiers.Ctrl) },
        };
        Assert.True(registry.TryRegister(new GlobalShortcutInfo("app", "shot", "", "", "LOGO+a")));
        Assert.False(registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: true));
        Assert.True(registry.HandleKey(48, 0x62, Modifiers.Ctrl, pressed: true));
    }

    [Theory]
    [InlineData(new uint[] { SuperL, LowerA }, LowerA, Modifiers.Super)]
    [InlineData(new uint[] { 0x41, 0xffe3, 0xffe1 }, LowerA, Modifiers.Ctrl | Modifiers.Shift)]
    [InlineData(new uint[] { SuperL }, SuperL, Modifiers.None)]
    [InlineData(new uint[] { 0xffe9, 0xffc9 }, 0xffc9, Modifiers.Alt)]
    public void A_keyboard_trigger_maps_onto_a_chord(uint[] keysyms, uint keysym, Modifiers modifiers)
    {
        Assert.True(ActionBindingManager.TryChord(keysyms, out var mappedKeysym, out var mappedModifiers));
        Assert.Equal(keysym, mappedKeysym);
        Assert.Equal(modifiers, mappedModifiers);
    }

    [Fact]
    public void A_trigger_with_two_plain_keys_is_refused()
    {
        Assert.False(ActionBindingManager.TryChord([LowerA, 0x62], out _, out _));
        Assert.False(ActionBindingManager.TryChord([], out _, out _));
    }

    [Theory]
    [InlineData(false, 0u)]
    [InlineData(true, 0u)]
    [InlineData(false, 1u)]
    public void A_binding_sends_triggered_when_its_chord_is_pressed(bool refused, uint armFlags)
    {
        using var host = new CompositorTestHost();
        var registry = new PreferredTriggerShortcuts();
        using var manager = new ActionBindingManager(host.Display, registry, new FixedPolicy(!refused));
        Basin.Hypr.Protocol.GamescopeActionBindingManager? proxy = null;
        var wlRegistry = host.Client.Display.GetRegistry();
        wlRegistry.Global += (_, e) =>
        {
            if (e.Interface == "gamescope_action_binding_manager")
            {
                proxy = wlRegistry.Bind<Basin.Hypr.Protocol.GamescopeActionBindingManager>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        var binding = proxy!.CreateActionBinding();
        var triggered = new List<(uint Sequence, uint Flags)>();
        binding.Triggered += (_, e) => triggered.Add((e.Sequence, (uint)e.TriggerFlags));
        binding.SetDescription("test");
        binding.AddKeyboardTrigger(BitConverter.GetBytes(SuperL).Concat(BitConverter.GetBytes(LowerA)).ToArray());
        binding.Arm((Basin.Hypr.Protocol.GamescopeActionBinding.ArmFlag)armFlags);
        host.PumpToServer();

        Assert.Equal(refused ? 0 : 1, manager.Armed);
        for (var press = 0; press < 2; press++)
        {
            registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: true);
            registry.HandleKey(KeyA, LowerA, Modifiers.Super, pressed: false);
        }

        host.PumpToClient();
        var expected = refused ? 0 : armFlags == 1 ? 1 : 2;
        Assert.Equal(expected, triggered.Count);
        Assert.All(triggered, entry => Assert.Equal(1u, entry.Flags));
        if (expected == 2)
        {
            Assert.Equal(triggered[0].Sequence + 1, triggered[1].Sequence);
        }

        binding.Disarm();
        host.PumpToServer();
        Assert.Equal(0, manager.Armed);
        Assert.Equal(0, registry.Count);
    }

    private sealed class FixedPolicy(bool allowed) : IActionBindingPolicy
    {
        public bool Allows(WlClient client) => allowed;
    }

    private sealed class RecordingObserver : IGlobalShortcutObserver
    {
        public List<string> Log { get; } = [];

        public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
        {
        }

        public void ShortcutRemoved(in GlobalShortcutInfo shortcut) => Log.Add($"removed {shortcut.AppId}:{shortcut.Id}");

        public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Log.Add($"on {shortcut.AppId}:{shortcut.Id}");

        public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs) => Log.Add($"off {shortcut.AppId}:{shortcut.Id}");
    }
}
