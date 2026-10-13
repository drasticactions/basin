using Basin.Desktop;
using Basin.Desktop.Protocol;
using Basin.Tests;
using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "basin-kb-session-" + Guid.NewGuid().ToString("N"), "keyboard.json");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_state)))
        {
            Directory.Delete(Path.GetDirectoryName(_state)!, recursive: true);
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig(SoftKeyboardSessionOptions options)
        {
            Host = new CompositorTestHost();
            Session = new SoftKeyboardSession(Host.Loop, options);
            Relay = new InputMethodRelay(Host.Display, Host.Seat, Session.TextInput);
            Manager = new TextInputManager(Host.Display, Host.Seat, Relay);
            Session.Attach(Host.Seat.Keyboard, Manager, Relay);
            Session.WantedChanged += (wanted, why) => Changes.Add($"{(wanted ? "on" : "off")} {why}");
            Window = MappedToplevel.Map(Host, Host.Client);
            var registry = Host.Client.Display.GetRegistry();
            ZwpTextInputManagerV3? bound = null;
            registry.Global += (_, e) =>
            {
                if (e.Interface == "zwp_text_input_manager_v3")
                {
                    bound = registry.Bind<ZwpTextInputManagerV3>(e.Name, 1);
                }
            };
            Host.PumpToClient();
            TextInput = bound!.GetTextInput(Host.Client.Seat!);
            TextInput.Enter += (_, _) => Entered++;
        }

        public CompositorTestHost Host { get; }

        public SoftKeyboardSession Session { get; }

        public InputMethodRelay Relay { get; }

        public TextInputManager Manager { get; }

        public MappedToplevel Window { get; }

        public ZwpTextInputV3 TextInput { get; }

        public int Entered { get; private set; }

        public List<string> Changes { get; } = [];

        public void Focus()
        {
            Host.Seat.Keyboard.NotifyEnter(Window.ServerSurface);
            Host.PumpUntil(() => Entered == 1);
        }

        public void Enable()
        {
            TextInput.Enable();
            TextInput.Commit();
            Host.PumpToServer();
        }

        public void Disable()
        {
            TextInput.Disable();
            TextInput.Commit();
            Host.PumpToServer();
        }

        public void Dispose()
        {
            Session.Dispose();
            Manager.Dispose();
            Relay.Dispose();
            Host.Dispose();
        }
    }

    [Fact]
    public void On_touch_focus_a_field_raises_the_keyboard_only_after_a_touch_and_a_hardware_key_hides_it()
    {
        using var rig = new Rig(new SoftKeyboardSessionOptions());
        var session = rig.Session;
        rig.Focus();
        session.NotePointer();
        rig.Enable();
        Assert.NotNull(session.TextInput.ActiveSurface);
        Assert.False(session.Wanted);

        session.NoteTouch(rig.Window.ServerSurface);
        Assert.True(session.Automatic);
        Assert.Equal(LastInput.Touch, session.LastInput);

        session.NoteHardwareKey(pressed: false);
        Assert.True(session.Wanted);
        session.NoteHardwareKey(pressed: true);
        Assert.False(session.Wanted);
        Assert.Equal(LastInput.Keyboard, session.LastInput);

        rig.Disable();
        session.NoteInput(LastInput.Touch);
        rig.Enable();
        Assert.True(session.Wanted);
        rig.Disable();
        Assert.False(session.Wanted);
        Assert.Contains("on touch", rig.Changes);
        Assert.Contains("off key", rig.Changes);
        Assert.Contains("on activate", rig.Changes);
        Assert.Contains("off deactivate", rig.Changes);
    }

    [Fact]
    public void On_any_focus_a_field_raises_the_keyboard_after_a_pointer_too_and_manual_survives_deactivation()
    {
        using var rig = new Rig(new SoftKeyboardSessionOptions { Policy = SoftKeyboardPolicy.OnAnyFocus });
        rig.Focus();
        rig.Session.NotePointer();
        rig.Enable();
        Assert.True(rig.Session.Wanted);
        rig.Session.Raise();
        rig.Disable();
        Assert.True(rig.Session.Manual);
        Assert.True(rig.Session.Wanted);
        rig.Session.Hide("test");
        Assert.False(rig.Session.Wanted);
        Assert.Equal("off test", rig.Changes[^1]);
    }

    [Fact]
    public void The_layout_and_size_are_remembered_in_the_state_file()
    {
        var options = new SoftKeyboardSessionOptions
        {
            Layouts = ["win8-standard-en-us", "win8-thumb-en-us", "win8-full-en-us"],
            StatePath = _state,
        };
        using (var rig = new Rig(options))
        {
            Assert.Equal("win8-standard-en-us", rig.Session.CurrentLayout.Id);
            Assert.NotNull(rig.Session.Engine.Pages);
            var selected = new List<string>();
            rig.Session.LayoutSelected += layout => selected.Add(layout.Id);
            rig.Session.Engine.TapCell(1, rig.Session.Typer.Find("hide"), 0);
            Assert.True(rig.Session.Engine.Grid.Rows == 4);
            Assert.True(rig.Session.Engine.OpenLongPress(rig.Session.Typer.Find("hide")));
            rig.Session.Engine.ChooseLongPress(2, 100);
            Assert.Equal(["win8-thumb-en-us"], selected);
            Assert.Equal(17, rig.Session.Engine.Grid.Columns);
            rig.Session.ChooseSize(KeyboardSize.Large);
        }

        Assert.Contains("\"layout\": \"win8-thumb-en-us\"", File.ReadAllText(_state), StringComparison.Ordinal);
        using var again = new Rig(options);
        Assert.Equal("win8-thumb-en-us", again.Session.CurrentLayout.Id);
        Assert.Equal(KeyboardSize.Large, again.Session.Size);
        Assert.True(again.Session.SizeChosen);
        Assert.Equal("thumb", SoftKeyboardSession.ShapeOf(again.Session.CurrentLayout));
    }

    [Fact]
    public void The_xkb_shapes_are_built_from_the_seat_keymap_once_attached()
    {
        using var rig = new Rig(new SoftKeyboardSessionOptions { Layouts = [SoftKeyboardSessionOptions.XkbStandard, SoftKeyboardSessionOptions.XkbFull] });
        rig.Host.Seat.Keyboard.SetKeymap(new Basin.Capabilities.KeymapNames { Layout = "de" });
        Assert.Equal(2, rig.Session.Layouts.Count);
        Assert.Contains("ü", string.Concat(Enumerable.Range(0, rig.Session.Engine.Grid.CellCount).Select(i => rig.Session.Engine.Grid[i].Plain?.Text)), StringComparison.Ordinal);
        Assert.Equal(SoftKeyboardSessionOptions.XkbStandard, rig.Session.CurrentLayout.Id);
        Assert.Equal(13, rig.Session.Engine.Grid.Columns);
        Assert.Equal("full", SoftKeyboardSession.ShapeOf(rig.Session.Layouts[1]));
    }
}
