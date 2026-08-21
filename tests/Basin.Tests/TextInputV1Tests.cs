using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class TextInputV1Tests
{
    [Fact]
    public void One_input_method_drives_a_v1_client_through_the_whole_cycle()
    {
        using var host = new CompositorTestHost();
        using var relay = new InputMethodRelay(host.Display, host.Seat);
        using var manager = new TextInputV1Manager(host.Display, host.Seat, relay);
        var window = MappedToplevel.Map(host, host.Client);

        var (tiManager, imManager) = BindBoth(host);
        var textInput = tiManager.CreateTextInput();
        var entered = new List<uint>();
        var left = 0;
        var preedits = new List<(string Text, string Commit)>();
        var preeditCursors = new List<int>();
        var commits = new List<string>();
        var deletions = new List<(int Index, uint Length)>();
        textInput.Enter += (_, _) => entered.Add(1);
        textInput.Leave += (_, _) => left++;
        textInput.PreeditCursor += (_, e) => preeditCursors.Add(e.Index);
        textInput.PreeditString += (_, e) => preedits.Add((e.Text, e.Commit));
        textInput.CommitString += (_, e) => commits.Add(e.Text);
        textInput.DeleteSurroundingText += (_, e) => deletions.Add((e.Index, e.Length));

        var inputMethod = imManager.GetInputMethod(host.Client.Seat!);
        var activated = 0;
        var deactivated = 0;
        var surroundings = new List<string>();
        var contentTypes = new List<(uint Hint, uint Purpose)>();
        inputMethod.Activate += (_, _) => activated++;
        inputMethod.Deactivate += (_, _) => deactivated++;
        inputMethod.SurroundingText += (_, e) => surroundings.Add(e.Text);
        inputMethod.ContentType += (_, e) => contentTypes.Add(((uint)e.Hint, (uint)e.Purpose));
        host.PumpToServer();

        textInput.Activate(host.Client.Seat!, window.Surface);
        host.PumpToClient();
        Assert.Empty(entered);
        Assert.Equal(0, activated);

        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        host.PumpUntil(() => entered.Count == 1 && activated == 1);

        textInput.SetSurroundingText("hello", 5, 5);
        textInput.SetContentType(
            Basin.Desktop.Protocol.ZwpTextInputV1.ContentHint.AutoCompletion,
            Basin.Desktop.Protocol.ZwpTextInputV1.ContentPurpose.Date);
        textInput.CommitState(7);
        host.PumpUntil(() => surroundings.Count == 1 && contentTypes.Count == 1);
        Assert.Equal("hello", surroundings[0]);

        Assert.Equal((0x1u, 10u), contentTypes[0]);

        inputMethod.SetPreeditString("にほ", 3, 3);
        inputMethod.Commit(1);
        host.PumpUntil(() => preedits.Count == 1);
        Assert.Equal("にほ", preedits[0].Text);
        Assert.Equal(3, preeditCursors[0]);

        Assert.Equal(string.Empty, preedits[0].Commit);

        inputMethod.DeleteSurroundingText(2, 4);
        inputMethod.CommitString("日本語");
        inputMethod.Commit(2);
        host.PumpUntil(() => commits.Count == 1 && deletions.Count == 1);
        Assert.Equal("日本語", commits[0]);

        Assert.Equal((-2, 6u), deletions[0]);

        host.Seat.Keyboard.NotifyEnter(null);
        host.PumpUntil(() => left == 1 && deactivated >= 1);

        textInput.Dispose();
        inputMethod.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_v1_client_and_a_v3_client_are_served_by_the_same_method()
    {
        using var host = new CompositorTestHost();
        using var relay = new InputMethodRelay(host.Display, host.Seat);
        using var v1 = new TextInputV1Manager(host.Display, host.Seat, relay);
        using var v3 = new TextInputManager(host.Display, host.Seat, relay);
        var window = MappedToplevel.Map(host, host.Client);

        var (v1Manager, imManager) = BindBoth(host);
        var v3Manager = Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(host, "zwp_text_input_manager_v3");

        var v1Input = v1Manager.CreateTextInput();
        var v1Preedits = new List<string>();
        v1Input.PreeditString += (_, e) => v1Preedits.Add(e.Text);

        var v3Input = v3Manager.GetTextInput(host.Client.Seat!);
        var v3Preedits = new List<string?>();
        v3Input.PreeditString += (_, e) => v3Preedits.Add(e.Text);

        var inputMethod = imManager.GetInputMethod(host.Client.Seat!);
        var activated = 0;
        inputMethod.Activate += (_, _) => activated++;
        host.PumpToServer();

        v1Input.Activate(host.Client.Seat!, window.Surface);
        host.Seat.Keyboard.NotifyEnter(window.ServerSurface);
        v3.NotifyFocus(window.ServerSurface);
        v3Input.Enable();
        v3Input.Commit();
        host.PumpUntil(() => activated >= 1);

        inputMethod.SetPreeditString("composing", 0, 9);
        inputMethod.Commit(1);
        host.PumpUntil(() => v1Preedits.Count == 1 && v3Preedits.Count == 1);

        Assert.Equal("composing", v1Preedits[0]);
        Assert.Equal("composing", v3Preedits[0]);

        v1Input.Dispose();
        v3Input.Dispose();
        inputMethod.Dispose();
        host.PumpToServer();
    }

    private static (Basin.Desktop.Protocol.ZwpTextInputManagerV1 TextInput, Basin.Desktop.Protocol.ZwpInputMethodManagerV2 InputMethod) BindBoth(
        CompositorTestHost host) =>
        (Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV1>(host, "zwp_text_input_manager_v1"),
         Bind<Basin.Desktop.Protocol.ZwpInputMethodManagerV2>(host, "zwp_input_method_manager_v2"));

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
        Assert.NotNull(proxy);
        return proxy!;
    }
}
