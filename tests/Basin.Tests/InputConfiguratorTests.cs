using Basin.Capabilities;
using Basin.Desktop;
using Basin.Seat.Backends;
using Xunit;

namespace Basin.Tests;

public sealed class InputConfiguratorTests
{
    private static InputRule Rule(InputMatch match, params (InputSetting, InputSettingValue)[] settings) =>
        new() { Match = match, Settings = settings };

    private static (InputSetting, InputSettingValue) Flag(InputSetting setting, bool on) => (setting, InputSettingValue.Flag(on));

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Host = new CompositorTestHost(200, 100);
            Binder = new SeatBinder(Host.Seat, Host.Layout, new LayoutPointer(Host.Layout), new CursorController(Host.Layout));
        }

        public CompositorTestHost Host { get; }

        public SeatBinder Binder { get; }

        public FakeInputDeviceConfiguration Backend { get; } = new();

        public InputConfigurator Configurator { get; } = new();

        public List<InputRuleOutcome> Rejected { get; } = [];

        public void Attach()
        {
            Configurator.Rejected += Rejected.Add;
            Configurator.Attach(Host.Seat, Binder, Backend);
        }

        public void Dispose()
        {
            Configurator.Dispose();
            Host.Dispose();
        }
    }

    [Fact]
    public void Rules_merge_per_key_and_the_most_specific_wins()
    {
        using var rig = new Rig();
        var touchpad = rig.Backend.AddTouchpad();
        var mouse = rig.Backend.AddMouse();
        rig.Configurator.Apply(
        [
            Rule(default, Flag(InputSetting.NaturalScroll, true), (InputSetting.AccelSpeed, InputSettingValue.Number(0.2))),
            Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true), (InputSetting.AccelSpeed, InputSettingValue.Number(0.4))),
            Rule(new InputMatch(NameGlob: "Logitech*"), (InputSetting.AccelProfile, InputSettingValue.Of(InputAccelProfile.Flat))),
            Rule(new InputMatch(Vendor: 0x046d, Product: 0xc08b), (InputSetting.AccelSpeed, InputSettingValue.Number(-0.3))),
        ]);
        rig.Attach();

        Assert.True(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);
        Assert.True(rig.Backend.Current(touchpad, InputSetting.NaturalScroll).IsOn);
        Assert.Equal(0.4, rig.Backend.Current(touchpad, InputSetting.AccelSpeed)[0]);
        Assert.True(rig.Backend.Current(mouse, InputSetting.NaturalScroll).IsOn);
        Assert.Equal((uint)InputAccelProfile.Flat, rig.Backend.Current(mouse, InputSetting.AccelProfile).Value);
        Assert.Equal(-0.3, rig.Backend.Current(mouse, InputSetting.AccelSpeed)[0]);
        Assert.Equal([0, 2, 3], rig.Configurator.RulesFor(mouse));
        Assert.Equal(InputSettingLayer.Config, rig.Configurator.SourceOf(mouse, InputSetting.AccelSpeed));
        Assert.Equal(InputSettingLayer.Default, rig.Configurator.SourceOf(mouse, InputSetting.LeftHanded));
    }

    [Fact]
    public void A_later_rule_of_equal_specificity_wins()
    {
        using var rig = new Rig();
        var touchpad = rig.Backend.AddTouchpad();
        rig.Configurator.Apply(
        [
            Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true)),
            Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, false)),
        ]);
        rig.Attach();

        Assert.Empty(rig.Backend.Writes);
        Assert.False(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);
    }

    [Fact]
    public void A_device_added_after_apply_gets_its_rules()
    {
        using var rig = new Rig();
        rig.Configurator.Apply([Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.NaturalScroll, true))]);
        rig.Attach();
        var added = new List<ulong>();
        rig.Configurator.DeviceAdded += info => added.Add(info.Id);

        var touchpad = rig.Backend.AddTouchpad();

        Assert.True(rig.Backend.Current(touchpad, InputSetting.NaturalScroll).IsOn);
        Assert.Equal([touchpad], added);
    }

    [Fact]
    public void A_key_removed_from_the_rules_returns_to_the_default()
    {
        using var rig = new Rig();
        var touchpad = rig.Backend.AddTouchpad();
        rig.Configurator.Apply([Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true), Flag(InputSetting.NaturalScroll, true))]);
        rig.Attach();
        Assert.True(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);

        rig.Configurator.Apply([Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.NaturalScroll, true))]);

        Assert.False(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);
        Assert.True(rig.Backend.Current(touchpad, InputSetting.NaturalScroll).IsOn);
        Assert.Equal(InputSettingLayer.Default, rig.Configurator.SourceOf(touchpad, InputSetting.Tap));
    }

    [Fact]
    public void A_runtime_set_survives_apply_and_reset_restores_the_rule_value()
    {
        using var rig = new Rig();
        var touchpad = rig.Backend.AddTouchpad();
        var rules = new[] { Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true)) };
        rig.Configurator.Apply(rules);
        rig.Attach();
        var changes = new List<(ulong, InputSetting)>();
        rig.Configurator.Changed += (id, setting) => changes.Add((id, setting));

        Assert.Equal(InputSettingResult.Success, rig.Configurator.Set(touchpad, InputSetting.Tap, InputSettingValue.Flag(false)));
        Assert.Equal(InputSettingLayer.Runtime, rig.Configurator.SourceOf(touchpad, InputSetting.Tap));
        Assert.Equal([(touchpad, InputSetting.Tap)], changes);

        rig.Configurator.Apply(rules);
        Assert.False(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);

        Assert.Equal(InputSettingResult.Success, rig.Configurator.Reset(touchpad, InputSetting.Tap));
        Assert.True(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);
        Assert.Equal(InputSettingLayer.Config, rig.Configurator.SourceOf(touchpad, InputSetting.Tap));
    }

    [Fact]
    public void A_runtime_value_is_dropped_with_its_device()
    {
        using var rig = new Rig();
        rig.Attach();
        var touchpad = rig.Backend.AddTouchpad();
        rig.Configurator.Set(touchpad, InputSetting.Tap, InputSettingValue.Flag(true));

        rig.Backend.Remove(touchpad);
        var again = rig.Backend.AddTouchpad();

        Assert.NotEqual(touchpad, again);
        Assert.False(rig.Backend.Current(again, InputSetting.Tap).IsOn);
        Assert.Equal(InputSettingLayer.Default, rig.Configurator.SourceOf(again, InputSetting.Tap));
    }

    [Fact]
    public void An_unsupported_key_reports_once_per_rule_and_device_and_is_never_read()
    {
        using var rig = new Rig();
        var mouse = rig.Backend.AddMouse();
        rig.Configurator.Apply([Rule(default, Flag(InputSetting.Tap, true))]);
        rig.Attach();
        rig.Configurator.Apply([Rule(default, Flag(InputSetting.Tap, true))]);
        rig.Backend.AddKeyboard();

        Assert.Empty(rig.Backend.UnsupportedReads);
        var outcomes = rig.Rejected.Where(o => o.Device == mouse).ToList();
        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(InputSettingResult.Unsupported, o.Result));
        Assert.Equal(3, rig.Rejected.Count);
    }

    [Fact]
    public void A_refused_value_reports_invalid()
    {
        using var rig = new Rig();
        var mouse = rig.Backend.AddMouse();
        rig.Backend.Refuse = (_, setting, _) => setting == InputSetting.AccelSpeed;
        rig.Configurator.Apply([Rule(default, (InputSetting.AccelSpeed, InputSettingValue.Number(0.5)))]);
        rig.Attach();

        var outcome = Assert.Single(rig.Rejected);
        Assert.Equal(InputSettingResult.Invalid, outcome.Result);
        Assert.Equal(mouse, outcome.Device);
    }

    [Fact]
    public void A_rejected_outcome_names_the_rule_index_behind_a_catch_all()
    {
        using var rig = new Rig();
        var mouse = rig.Backend.AddMouse();
        rig.Backend.Refuse = (_, setting, _) => setting == InputSetting.AccelSpeed;
        rig.Configurator.Apply(
        [
            Rule(default, Flag(InputSetting.NaturalScroll, true)),
            Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true)),
            Rule(new InputMatch(Type: InputDeviceType.Pointer), (InputSetting.AccelSpeed, InputSettingValue.Number(0.5))),
        ]);
        rig.Attach();

        var outcome = Assert.Single(rig.Rejected, o => o.Setting == InputSetting.AccelSpeed);
        Assert.Equal(2, outcome.Rule);
        Assert.Equal(mouse, outcome.Device);
    }

    [Fact]
    public void No_write_happens_when_the_resolved_value_is_current()
    {
        using var rig = new Rig();
        rig.Backend.AddTouchpad();
        rig.Backend.AddMouse();
        rig.Configurator.Apply([Rule(default, Flag(InputSetting.NaturalScroll, false), Flag(InputSetting.LeftHanded, false))]);
        rig.Attach();
        rig.Configurator.Apply([]);

        Assert.Empty(rig.Backend.Writes);
    }

    [Fact]
    public void Before_attach_it_enumerates_nothing_and_refuses_sets()
    {
        using var rig = new Rig();
        var touchpad = rig.Backend.AddTouchpad();
        rig.Configurator.Apply([Rule(default, Flag(InputSetting.Tap, true))]);

        Assert.Equal(0, rig.Configurator.Enumerate(new InputDeviceInfo[4]));
        Assert.Equal(InputSettingResult.Unsupported, rig.Configurator.Set(touchpad, InputSetting.Tap, InputSettingValue.Flag(true)));
        Assert.Empty(rig.Backend.Writes);

        rig.Attach();
        Assert.True(rig.Backend.Current(touchpad, InputSetting.Tap).IsOn);
        Assert.Throws<InvalidOperationException>(() => rig.Configurator.Attach(rig.Host.Seat, rig.Binder, rig.Backend));
    }

    [Fact]
    public void Keyboard_rules_set_the_seat_fallback_and_a_named_device_its_own_repeat()
    {
        using var rig = new Rig();
        var laptop = rig.Backend.AddKeyboard("AT Translated Set 2 keyboard");
        var external = rig.Backend.AddKeyboard("Keychron K2");
        rig.Configurator.Apply(
        [
            Rule(new InputMatch(AnyKeyboard: true), (InputSetting.RepeatRate, InputSettingValue.Of(50u)), (InputSetting.RepeatDelay, InputSettingValue.Of(300u))),
            Rule(new InputMatch(NameGlob: "Keychron*"), (InputSetting.RepeatRate, InputSettingValue.Of(10u))),
        ]);
        rig.Attach();

        Assert.Equal((50, 300), rig.Host.Seat.Keyboard.SeatRepeatInfo);
        Assert.Null(rig.Configurator.KeyboardDeviceFor(laptop));
        var keyboard = rig.Configurator.KeyboardDeviceFor(external);
        Assert.NotNull(keyboard);

        rig.Host.Seat.Keyboard.Activate(keyboard);
        Assert.Equal((10, 300), rig.Host.Seat.Keyboard.RepeatInfo);
        rig.Host.Seat.Keyboard.Activate(null);
        Assert.Equal((50, 300), rig.Host.Seat.Keyboard.RepeatInfo);

        Assert.True(rig.Configurator.TryGet(external, InputSetting.RepeatRate, out var rate));
        Assert.Equal(10u, rate.Value);
        Assert.Equal(InputSettingLayer.Config, rig.Configurator.SourceOf(external, InputSetting.RepeatRate));

        rig.Configurator.Apply([]);
        Assert.Equal((25, 400), rig.Host.Seat.Keyboard.SeatRepeatInfo);
    }

    [Fact]
    public void A_scroll_factor_is_a_runtime_setting_on_pointers_only()
    {
        using var rig = new Rig();
        var mouse = rig.Backend.AddMouse();
        var keyboard = rig.Backend.AddKeyboard();
        rig.Attach();

        Assert.Equal(InputSettingResult.Success, rig.Configurator.Set(mouse, InputSetting.ScrollFactor, InputSettingValue.Number(2.5)));
        Assert.Equal(2.5, rig.Configurator.ScrollFactorFor(mouse));
        Assert.Equal(InputSettingResult.Invalid, rig.Configurator.Set(mouse, InputSetting.ScrollFactor, InputSettingValue.Number(-1)));
        Assert.Equal(InputSettingResult.Unsupported, rig.Configurator.Set(keyboard, InputSetting.ScrollFactor, InputSettingValue.Number(2)));

        Assert.Equal(InputSettingResult.Success, rig.Configurator.Reset(mouse, InputSetting.ScrollFactor));
        Assert.Equal(1.0, rig.Configurator.ScrollFactorFor(mouse));
    }

    [Fact]
    public void Without_a_backend_libinput_keys_report_once_per_rule_and_keyboard_keys_still_apply()
    {
        using var host = new CompositorTestHost(200, 100);
        var binder = new SeatBinder(host.Seat, host.Layout, new LayoutPointer(host.Layout), new CursorController(host.Layout));
        using var configurator = new InputConfigurator();
        var rejected = new List<InputRuleOutcome>();
        configurator.Rejected += rejected.Add;
        configurator.Apply(
        [
            Rule(new InputMatch(Type: InputDeviceType.Touchpad), Flag(InputSetting.Tap, true), Flag(InputSetting.NaturalScroll, true)),
            Rule(new InputMatch(AnyKeyboard: true), (InputSetting.RepeatRate, InputSettingValue.Of(40u))),
        ]);
        configurator.Attach(host.Seat, binder, backend: null);

        Assert.Equal(2, rejected.Count);
        Assert.All(rejected, o => Assert.Equal((0, 0ul, InputSettingResult.Unsupported), (o.Rule, o.Device, o.Result)));
        Assert.Equal(40, host.Seat.Keyboard.SeatRepeatInfo.Rate);
        Assert.True(configurator.NamesSeatRepeat);
        Assert.False(configurator.NamesSeatKeymap);
    }
}
