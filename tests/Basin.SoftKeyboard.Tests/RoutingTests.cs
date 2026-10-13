using Basin.Desktop;
using Basin.Desktop.Protocol;
using Basin.Tests;
using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class RoutingTests
{
    private const uint AutoCap = 0x4;

    [Theory]
    [InlineData(AutoCap, 0u, "", 0, true)]
    [InlineData(AutoCap, 0u, "Hello", 5, false)]
    [InlineData(AutoCap, 0u, "Hello. ", 7, true)]
    [InlineData(AutoCap, 0u, "Hi! ", 4, true)]
    [InlineData(AutoCap, 0u, "Why? ", 5, true)]
    [InlineData(AutoCap, 0u, "Line\n", 5, true)]
    [InlineData(AutoCap, 0u, "Hello.", 6, false)]
    [InlineData(AutoCap, 0u, "a, ", 3, false)]
    [InlineData(0u, 0u, "", 0, false)]
    [InlineData(AutoCap, 6u, "", 0, false)]
    [InlineData(AutoCap, 8u, "", 0, false)]
    [InlineData(AutoCap, 7u, "", 0, true)]
    [InlineData(AutoCap | 0x8, 0u, "", 0, false)]
    [InlineData(0x10u, 6u, "abc", 3, true)]
    [InlineData(AutoCap, 0u, "Für. ", 6, true)]
    [InlineData(AutoCap, 0u, "Für. x", 6, true)]
    public void Auto_cap_follows_the_hint_purpose_and_text_before_the_cursor(uint hint, uint purpose, string text, int cursor, bool expected) =>
        Assert.Equal(expected, FieldState.AutoCapFor(hint, purpose, text, cursor));

    [Theory]
    [InlineData(0u, FieldKind.Text)]
    [InlineData(5u, FieldKind.Url)]
    [InlineData(6u, FieldKind.Email)]
    [InlineData(3u, FieldKind.Number)]
    [InlineData(4u, FieldKind.Phone)]
    [InlineData(8u, FieldKind.Password)]
    [InlineData(13u, FieldKind.Terminal)]
    [InlineData(10u, FieldKind.Date)]
    public void A_purpose_maps_to_a_field_kind(uint purpose, FieldKind kind) =>
        Assert.Equal(kind, FieldState.KindOf(purpose));

    [Fact]
    public void Without_a_text_input_text_is_typed_as_keys_and_double_space_is_backspace_then_period()
    {
        var output = new RecordingOutput();
        var router = new KeyboardRouter(new SoftKeyboardTextInput(), output);
        Assert.Equal(KeyRoute.Keys, router.Route);
        Send(router, "h", 0);
        Send(router, "i", 100);
        Send(router, " ", 200);
        Send(router, " ", 900);
        Send(router, " ", 1000);
        Assert.Equal("hi [14].  ", output.Log);
    }

    [Fact]
    public void Double_space_needs_the_second_space_within_1000_ms_and_no_punctuation_before()
    {
        var output = new RecordingOutput();
        var router = new KeyboardRouter(new SoftKeyboardTextInput(), output);
        Send(router, "a", 0);
        Send(router, " ", 100);
        Send(router, " ", 1100);
        Send(router, ",", 2000);
        Send(router, " ", 2100);
        Send(router, " ", 2200);
        Assert.Equal("a  ,  ", output.Log);
        router.SpacesToPeriod = false;
        Send(router, "b", 3000);
        Send(router, " ", 3100);
        Send(router, " ", 3200);
        Assert.EndsWith("b  ", output.Log);
    }

    [Fact]
    public void Function_keys_and_word_delete_go_out_as_keys_without_a_text_input()
    {
        var output = new RecordingOutput();
        var router = new KeyboardRouter(new SoftKeyboardTextInput(), output);
        router.Send(new TypedKey(KeyCap.Backspace, false, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Return, false, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Tab, false, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Left, false, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Backspace, false, false, true, true), 0);
        router.Send(new TypedKey(KeyCap.Character(".com"), false, false, false, false), 0);
        Assert.Equal("[14][28][15][105][ctrl+14].com", output.Log);
    }

    [Fact]
    public void A_backend_that_is_active_takes_every_key_as_keysyms()
    {
        var output = new RecordingOutput();
        var backend = new RecordingBackend();
        var router = new KeyboardRouter(new SoftKeyboardTextInput(), output) { Backend = backend };
        Assert.Equal(KeyRoute.Keys, router.Route);
        backend.IsActive = true;
        Assert.Equal(KeyRoute.Backend, router.Route);
        router.Send(new TypedKey(KeyCap.Character("a"), true, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Backspace, false, false, false, false), 0);
        router.Send(new TypedKey(KeyCap.Backspace, false, false, true, true), 0);
        router.Send(new TypedKey(KeyCap.Character(".c"), false, false, false, false), 0);
        Assert.Equal(
            ["0x41 down", "0x41 up", "0xff08 down", "0xff08 up", "0xff08 down ctrl", "0xff08 up ctrl", "0x2e down", "0x2e up", "0x63 down", "0x63 up"],
            backend.Log);
        Assert.Empty(output.Log);
        Assert.Equal(0x41u + 8, backend.Keycodes[0]);
        Assert.Equal(KeyboardRouter.BackspaceCode + 8, backend.Keycodes[2]);
    }

    [Fact]
    public void With_a_text_input_and_no_method_text_is_committed_and_keys_stay_keys()
    {
        using var host = new CompositorTestHost();
        var fallback = new SoftKeyboardTextInput();
        using var relay = new InputMethodRelay(host.Display, host.Seat, fallback);
        using var manager = new TextInputManager(host.Display, host.Seat, relay);
        var window = MappedToplevel.Map(host, host.Client);
        var textInput = Bind<ZwpTextInputManagerV3>(host, "zwp_text_input_manager_v3").GetTextInput(host.Client.Seat!);
        var commits = new List<string?>();
        var deletes = new List<uint>();
        var entered = 0;
        textInput.Enter += (_, _) => entered++;
        textInput.CommitString += (_, e) => commits.Add(e.Text);
        textInput.DeleteSurroundingText += (_, e) => deletes.Add(e.BeforeLength);
        var output = new RecordingOutput();
        var router = new KeyboardRouter(fallback, output);
        var engine = Keys.Engine();
        long now = 0;
        router.Attach(engine, () => now);

        manager.NotifyFocus(window.ServerSurface);
        host.PumpUntil(() => entered == 1);
        textInput.Enable();
        textInput.SetContentType(ZwpTextInputV3.ContentHint.AutoCapitalization, ZwpTextInputV3.ContentPurpose.Normal);
        textInput.SetSurroundingText(string.Empty, 0, 0);
        textInput.Commit();
        host.PumpToServer();
        Assert.Equal(KeyRoute.TextInput, router.Route);
        Assert.True(engine.State.AutoCap);

        Keys.Tap(engine, "h", now += 10);
        Keys.Tap(engine, "i", now += 100);
        textInput.SetSurroundingText("Hi there", 8, 8);
        textInput.Commit();
        host.PumpToServer();
        Assert.False(engine.State.AutoCap);
        Keys.Down(engine, "backspace", now += 100);
        engine.Tick(now + 1900);
        engine.Cancel(1);
        engine.TouchUp(1, now += 2000);
        Keys.Tap(engine, "return", now += 100);
        host.PumpUntil(() => commits.Count == 2 && deletes.Count == 1);
        Assert.Equal(["H", "i"], commits);
        Assert.Equal(5u, deletes[0]);
        Assert.Contains("[28]", output.Log);

        textInput.SetContentType(ZwpTextInputV3.ContentHint.None, ZwpTextInputV3.ContentPurpose.Email);
        textInput.Commit();
        host.PumpToServer();
        Assert.Equal(BottomRow.Email, fallback.Field.BottomRow);

        fallback.ExternalInputMethod = true;
        Assert.Equal(KeyRoute.Keys, router.Route);
        manager.NotifyFocus(null);
        host.PumpToServer();
        Assert.Null(fallback.ActiveSurface);
        Assert.False(fallback.Field.Active);
    }

    private static void Send(KeyboardRouter router, string text, long now) =>
        router.Send(new TypedKey(KeyCap.Character(text), false, false, false, false), now);

    private static T Bind<T>(CompositorTestHost host, string wireInterface)
        where T : Wayland.WlProxy, Wayland.IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, 1);
            }
        };
        host.PumpToClient();
        return proxy!;
    }

    private sealed class RecordingOutput : ISoftKeyOutput
    {
        public string Log { get; private set; } = string.Empty;

        public void Key(uint keycode, uint timeMs) => Log += $"[{keycode}]";

        public void ControlKey(uint keycode, uint timeMs) => Log += $"[ctrl+{keycode}]";

        public void ModifiedKey(uint keycode, uint xkbModifiers, KeyModifiers modifiers, uint timeMs) =>
            Log += $"[{modifiers}+{keycode}{(xkbModifiers != 0 ? "/" + xkbModifiers : string.Empty)}]";

        public int Type(string text, uint timeMs)
        {
            Log += text;
            return text.Length;
        }

        public bool TryResolve(uint keysym, out uint keycode, out uint modifiers)
        {
            keycode = keysym & 0xff;
            modifiers = 0;
            return true;
        }
    }

    private sealed class RecordingBackend : IKeyboardBackend
    {
        public bool IsActive { get; set; }

        public List<string> Log { get; } = [];

        public List<uint> Keycodes { get; } = [];

        public void ProcessKey(uint keysym, uint keycode, uint state, bool release, uint timeMs)
        {
            Log.Add($"0x{keysym:x} {(release ? "up" : "down")}{(state == 4 ? " ctrl" : string.Empty)}");
            Keycodes.Add(keycode);
        }
    }
}
