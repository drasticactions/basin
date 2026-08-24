using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class TextInputV2Tests
{
    private const uint ReasonChange = 0;
    private const uint ReasonFull = 1;
    private const uint ReasonReset = 2;
    private const uint ReasonEnter = 3;

    private sealed class RecordingMethod : ITextInputMethod
    {
        public bool IsAvailable => true;

        public bool HasKeyboardGrab => false;

        public Surface? Active { get; private set; }

        public int Activations { get; private set; }

        public int Deactivations { get; private set; }

        public int Commits { get; private set; }

        public List<string> Surroundings { get; } = [];

        public List<(uint Hint, uint Purpose)> ContentTypes { get; } = [];

        public List<Box> CursorRectangles { get; } = [];

        public event Action<PreeditString>? Preedit;

        public event Action<string>? CommitString;

        public event Action<uint, uint>? DeleteSurroundingText;

        public event Action? Done;

        public event Action? AvailabilityChanged;

        public void Activate(Surface surface)
        {
            Active = surface;
            Activations++;
        }

        public void Deactivate(Surface surface)
        {
            Active = null;
            Deactivations++;
        }

        public void SurroundingText(string text, uint cursor, uint anchor) => Surroundings.Add(text);

        public void ContentType(uint hint, uint purpose) => ContentTypes.Add((hint, purpose));

        public void CursorRectangle(in Box rect) => CursorRectangles.Add(rect);

        public void Commit(uint serial) => Commits++;

        public void ForwardKey(uint timeMs, uint keycode, bool pressed)
        {
        }

        public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
        {
        }

        public void RaisePreedit(string text, int begin, int end)
        {
            Preedit?.Invoke(new PreeditString(text, begin, end));
            Done?.Invoke();
        }

        public void RaiseCommit(string text) => CommitString?.Invoke(text);

        public void RaiseDelete(uint before, uint after) => DeleteSurroundingText?.Invoke(before, after);

        public void RaiseAvailable() => AvailabilityChanged?.Invoke();
    }

    private sealed class V2Client
    {
        public required Basin.Plasma.Protocol.ZwpTextInputV2 TextInput { get; init; }

        public List<uint> Enters { get; } = [];

        public List<uint> Leaves { get; } = [];

        public List<(string? Text, string? Commit)> Preedits { get; } = [];

        public List<(uint Index, uint Length, uint Style)> Stylings { get; } = [];

        public List<int> Cursors { get; } = [];

        public List<string?> Commits { get; } = [];

        public List<(uint Before, uint After)> Deletes { get; } = [];

        public List<byte[]> ModifiersMaps { get; } = [];

        public List<(uint State, int X, int Y, int W, int H)> PanelStates { get; } = [];

        public uint LastEnterSerial => Enters[^1];
    }

    private static V2Client BindV2(CompositorTestHost host, ShmTestClient? client = null)
    {
        client ??= host.Client;
        Basin.Plasma.Protocol.ZwpTextInputManagerV2? manager = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v2")
            {
                manager = registry.Bind<Basin.Plasma.Protocol.ZwpTextInputManagerV2>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(manager);

        var proxy = manager!.GetTextInput(client.Seat!);
        var result = new V2Client { TextInput = proxy };
        proxy.Enter += (_, e) => result.Enters.Add(e.Serial);
        proxy.Leave += (_, e) => result.Leaves.Add(e.Serial);
        proxy.PreeditString += (_, e) => result.Preedits.Add((e.Text, e.Commit));
        proxy.PreeditStyling += (_, e) => result.Stylings.Add((e.Index, e.Length, (uint)e.Style));
        proxy.PreeditCursor += (_, e) => result.Cursors.Add(e.Index);
        proxy.CommitString += (_, e) => result.Commits.Add(e.Text);
        proxy.DeleteSurroundingText += (_, e) => result.Deletes.Add((e.BeforeLength, e.AfterLength));
        proxy.ModifiersMap += (_, e) => result.ModifiersMaps.Add(e.Map.ToArray());
        proxy.InputPanelState += (_, e) => result.PanelStates.Add(((uint)e.State, e.X, e.Y, e.Width, e.Height));
        host.PumpToServer();
        return result;
    }

    [Fact]
    public void Enter_carries_a_fresh_serial_and_only_reaches_the_focused_client()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);
        var other = host.ConnectClient();

        var focusedClient = BindV2(host);
        var otherClient = BindV2(host, other);
        var window = MappedToplevel.Map(host, host.Client);

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        var enter = Assert.Single(focusedClient.Enters);
        Assert.NotEqual(0u, enter);
        Assert.Empty(otherClient.Enters);
    }

    [Fact]
    public void A_stale_serial_in_update_state_is_ignored_without_an_error()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        client.TextInput.Enable(window.Surface);
        host.PumpToServer();
        var baseline = method.Commits;

        client.TextInput.SetSurroundingText("stale", 5, 5);
        client.TextInput.UpdateState_(client.LastEnterSerial + 7, Basin.Plasma.Protocol.ZwpTextInputV2.UpdateState.Change);
        host.PumpToServer();

        Assert.Equal(baseline, method.Commits);
        Assert.Empty(method.Surroundings);

        client.TextInput.UpdateState_(client.LastEnterSerial, Basin.Plasma.Protocol.ZwpTextInputV2.UpdateState.Change);
        host.PumpToServer();
        Assert.Equal("stale", Assert.Single(method.Surroundings));

        var sync = host.Client.Display.Sync();
        var alive = false;
        sync.Done += (_, _) => alive = true;
        host.PumpUntil(() => alive);
    }

    [Theory]
    [InlineData(ReasonChange)]
    [InlineData(ReasonFull)]
    [InlineData(ReasonReset)]
    [InlineData(ReasonEnter)]
    public void Each_update_state_reason_pushes_the_state_and_commits(uint reason)
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        client.TextInput.Enable(window.Surface);
        host.PumpToServer();
        var baseline = method.Commits;

        client.TextInput.SetSurroundingText("text", 4, 4);
        client.TextInput.SetContentType(Basin.Plasma.Protocol.ZwpTextInputV2.ContentHint.None, Basin.Plasma.Protocol.ZwpTextInputV2.ContentPurpose.Terminal);
        client.TextInput.SetCursorRectangle(1, 2, 3, 4);
        client.TextInput.UpdateState_(client.LastEnterSerial, (Basin.Plasma.Protocol.ZwpTextInputV2.UpdateState)reason);
        host.PumpToServer();

        Assert.Equal("text", Assert.Single(method.Surroundings));
        Assert.Equal((0u, 13u), Assert.Single(method.ContentTypes));
        Assert.Equal(new Box(1, 2, 3, 4), Assert.Single(method.CursorRectangles));
        Assert.Equal(baseline + 1, method.Commits);
    }

    [Fact]
    public void A_preedit_arrives_with_one_styling_run_and_a_cursor()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();
        client.TextInput.Enable(window.Surface);
        host.PumpToServer();

        method.RaisePreedit("にほ", 0, 6);
        host.PumpToClient();

        Assert.Equal(("にほ", string.Empty), Assert.Single(client.Preedits));
        var styling = Assert.Single(client.Stylings);
        Assert.Equal(0u, styling.Index);
        Assert.Equal(6u, styling.Length);
        Assert.Equal((uint)Basin.Plasma.Protocol.ZwpTextInputV2.PreeditStyle.Highlight, styling.Style);
        Assert.Equal(0, Assert.Single(client.Cursors));

        method.RaisePreedit("に", 3, 3);
        host.PumpToClient();
        Assert.Equal(
            (uint)Basin.Plasma.Protocol.ZwpTextInputV2.PreeditStyle.Underline,
            client.Stylings[^1].Style);
    }

    [Fact]
    public void Commit_and_delete_surrounding_reach_the_client_unchanged()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();
        client.TextInput.Enable(window.Surface);
        host.PumpToServer();

        method.RaiseCommit("日本語");
        method.RaiseDelete(2, 1);
        host.PumpToClient();

        Assert.Equal("日本語", Assert.Single(client.Commits));
        Assert.Equal((2u, 1u), Assert.Single(client.Deletes));
    }

    [Fact]
    public void Modifiers_map_is_sent_on_enter_and_resent_on_a_keymap_change()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        var map = Assert.Single(client.ModifiersMaps);
        Assert.Equal("Shift\0Control\0Mod1\0Mod4\0"u8.ToArray(), map);

        host.Seat.Keyboard.SetKeymap();
        host.PumpToClient();
        Assert.Equal(2, client.ModifiersMaps.Count);
    }

    [Fact]
    public void Input_panel_state_reports_hidden_with_a_zero_rectangle()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();

        Assert.Equal((0u, 0, 0, 0, 0), Assert.Single(client.PanelStates));
    }

    [Fact]
    public void A_v2_client_and_a_v3_client_drive_the_same_relay_in_turn()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var v2Manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);
        using var v3Manager = new TextInputManager(host.Display, host.Seat, method);
        var v3ClientConnection = host.ConnectClient();

        var v2Client = BindV2(host);
        Basin.Desktop.Protocol.ZwpTextInputManagerV3? v3Factory = null;
        var registry = v3ClientConnection.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v3")
            {
                v3Factory = registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var v3TextInput = v3Factory!.GetTextInput(v3ClientConnection.Seat!);
        var v3Preedits = new List<string?>();
        v3TextInput.PreeditString += (_, e) => v3Preedits.Add(e.Text);
        host.PumpToServer();

        var v2Window = MappedToplevel.Map(host, host.Client);
        var v3Window = MappedToplevel.Map(host, v3ClientConnection);

        host.Seat.Keyboard.NotifyEnter(v2Window.ServerSurface);
        host.PumpToClient();
        v2Client.TextInput.Enable(v2Window.Surface);
        host.PumpToServer();
        Assert.Equal(1, method.Activations);

        method.RaisePreedit("v2", 0, 2);
        host.PumpToClient();
        Assert.Single(v2Client.Preedits);
        Assert.Empty(v3Preedits);

        host.Seat.Keyboard.NotifyEnter(v3Window.ServerSurface);
        v3Manager.NotifyFocus(v3Window.ServerSurface);
        host.PumpToClient();
        Assert.Equal(1, method.Deactivations);
        Assert.Single(v2Client.Leaves);

        v3TextInput.Enable();
        v3TextInput.Commit();
        host.PumpToServer();
        Assert.Equal(2, method.Activations);

        method.RaisePreedit("v3", 0, 2);
        host.PumpToClient();
        Assert.Single(v2Client.Preedits);
        Assert.Equal("v3", Assert.Single(v3Preedits));
    }

    [Fact]
    public void One_client_enabling_both_v2_and_v3_activates_the_relay_once()
    {
        using var host = new CompositorTestHost();
        using var relay = new InputMethodRelay(host.Display, host.Seat);
        using var v2Manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, relay);
        using var v3Manager = new TextInputManager(host.Display, host.Seat, relay);
        var imeConnection = host.ConnectClient();

        Basin.Desktop.Protocol.ZwpInputMethodManagerV2? imFactory = null;
        var imeRegistry = imeConnection.Display.GetRegistry();
        imeRegistry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_input_method_manager_v2")
            {
                imFactory = imeRegistry.Bind<Basin.Desktop.Protocol.ZwpInputMethodManagerV2>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var inputMethod = imFactory!.GetInputMethod(imeConnection.Seat!);
        var activations = 0;
        inputMethod.Activate += (_, _) => activations++;
        host.PumpToServer();

        var v2Client = BindV2(host);
        Basin.Desktop.Protocol.ZwpTextInputManagerV3? v3Factory = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwp_text_input_manager_v3")
            {
                v3Factory = registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(e.Name, 1);
            }
        };
        host.PumpToClient();
        var v3TextInput = v3Factory!.GetTextInput(host.Client.Seat!);
        host.PumpToServer();

        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        v3Manager.NotifyFocus(window.ServerSurface);
        host.PumpToClient();

        v2Client.TextInput.Enable(window.Surface);
        v3TextInput.Enable();
        v3TextInput.Commit();
        host.PumpToServer();
        host.PumpToClient();

        Assert.Equal(1, activations);
    }

    [Fact]
    public void Focus_out_sends_leave_with_a_serial_and_deactivates()
    {
        using var host = new CompositorTestHost();
        var method = new RecordingMethod();
        using var manager = new Basin.Plasma.TextInputV2Manager(host.Display, host.Seat, method);

        var client = BindV2(host);
        var window = MappedToplevel.Map(host, host.Client);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpToClient();
        client.TextInput.Enable(window.Surface);
        host.PumpToServer();
        Assert.Equal(1, method.Activations);

        host.Seat.Keyboard.NotifyEnter(null);
        host.PumpToClient();

        var leave = Assert.Single(client.Leaves);
        Assert.NotEqual(0u, leave);
        Assert.Equal(1, method.Deactivations);
    }
}
