using Basin.Desktop.Protocol;
using Basin.Sdl;
using SDL3;
using Wayland;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlTextInputTests
{
    [Fact]
    public void Composition_reaches_a_text_input_client_and_plain_typing_arrives_once()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var client = harness.Client;
        var manager = client.BindAt<ZwpTextInputManagerV3>("zwp_text_input_manager_v3", 1);
        var textInput = manager.GetTextInput(client.Seat!);
        var keyboard = client.Seat!.GetKeyboard();
        var entered = false;
        var preedits = new List<(string? Text, int Begin)>();
        var commits = new List<string?>();
        var keys = new List<uint>();
        textInput.Enter += (_, _) => entered = true;
        textInput.PreeditString += (_, e) => preedits.Add((e.Text, e.CursorBegin));
        textInput.CommitString += (_, e) => commits.Add(e.Text);
        keyboard.Key += (_, e) =>
        {
            if (e.State == WlKeyboard.KeyState.Pressed)
            {
                keys.Add(e.Key);
            }
        };
        harness.MapToplevel(200, 150, (data, stride) => { });
        var id = harness.Comp.Window.Window.Id;
        SdlEvents.Window(id, SDL.EventType.WindowFocusGained);
        harness.PumpUntil(() => entered, "the text input never entered");

        textInput.Enable();
        textInput.SetContentType(ZwpTextInputV3.ContentHint.None, ZwpTextInputV3.ContentPurpose.Normal);
        textInput.SetCursorRectangle(10, 20, 2, 16);
        textInput.Commit();
        var input = harness.Comp.TextInput;
        harness.PumpUntil(() => input.IsActive, "SDL text input never started");
        Assert.True(SDL.TextInputActive(harness.Comp.Window.Window.Handle));

        SdlEvents.TextEditing(id, "かん", 2);
        harness.PumpUntil(() => preedits.Count > 0, "the preedit never reached the client");
        Assert.Equal(("かん", 6), preedits[^1]);
        SdlEvents.TextInput(id, "漢");
        harness.PumpUntil(() => commits.Count > 0, "the commit never reached the client");
        Assert.Equal(["漢"], commits);

        SdlEvents.Key(id, SDL.Scancode.A, true);
        SdlEvents.TextInput(id, "a");
        SdlEvents.Key(id, SDL.Scancode.A, false);
        harness.PumpUntil(() => keys.Contains(30u), "the plain key never reached the client");
        harness.Pump();
        Assert.Equal(["漢"], commits);
        Assert.Equal(1, input.Dropped);

        textInput.Disable();
        textInput.Commit();
        harness.PumpUntil(() => !input.IsActive, "SDL text input never stopped");
        Assert.False(SDL.TextInputActive(harness.Comp.Window.Window.Handle));
    }

    [Fact]
    public void Content_types_map_onto_SDL_text_input_types()
    {
        Assert.Equal(4, SdlTextInput.InputType(8));
        Assert.Equal(2, SdlTextInput.InputType(6));
        Assert.Equal(6, SdlTextInput.InputType(3));
        Assert.Equal(7, SdlTextInput.InputType(9));
        Assert.Equal(0, SdlTextInput.InputType(13));
        Assert.Equal(3, SdlTextInput.Capitalization(0x10));
        Assert.Equal(1, SdlTextInput.Capitalization(0x4));
        Assert.Equal(0, SdlTextInput.Capitalization(0));
    }
}
