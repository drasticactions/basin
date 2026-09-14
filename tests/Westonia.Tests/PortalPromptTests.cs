using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basin;
using Basin.Capabilities;
using Basin.Config;
using Basin.Portal.Prompts.Avalonia;
using Xunit;

namespace Westonia.Tests;

public sealed class PortalPromptTests
{
    [AvaloniaFact]
    public void The_confirm_prompt_accepts_on_Enter_and_cancels_on_Escape()
    {
        var model = new ConfirmPromptModel(new ConfirmPrompt("org.example.App", "", true, "Take a screenshot?", "org.example.App wants to take a screenshot"));
        var responses = new List<PromptResponse>();
        model.Completed += responses.Add;
        var window = Show(new ConfirmPromptView { DataContext = model }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.ConfirmHeight);

        Assert.NotNull(Find<TextBlock>(window, t => t.Text == "Take a screenshot?"));
        Assert.NotNull(Find<TextBlock>(window, t => t.Text == "org.example.App is asking"));
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal([PromptResponse.Cancelled], responses);

        var again = new ConfirmPromptModel(new ConfirmPrompt("org.example.App", "", true, "Take a screenshot?", ""));
        again.Completed += responses.Add;
        Show(new ConfirmPromptView { DataContext = again }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.ConfirmHeight)
            .KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal([PromptResponse.Cancelled, PromptResponse.Accepted], responses);
    }

    [AvaloniaFact]
    public void The_source_prompt_lists_outputs_and_windows_and_needs_a_selection_to_share()
    {
        var prompt = new SourcePrompt("org.example.App", "", true, PromptSourceKinds.Monitor | PromptSourceKinds.Window, true, true,
            [new PromptOutput(null!, "HEADLESS-1", "Headless", new Box(0, 0, 160, 120), 1)],
            [new PromptToplevel(7, "Editor", "org.example.Editor"), new PromptToplevel(8, "Terminal", "org.example.Terminal")]);
        var model = new SourcePromptModel(in prompt);
        var window = Show(new SourcePromptView { DataContext = model }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.SourceHeight);

        Assert.Equal(3, model.Rows.Count);
        Assert.NotNull(Find<TextBlock>(window, t => t.Text == "HEADLESS-1"));
        Assert.NotNull(Find<TextBlock>(window, t => t.Text == "Terminal"));
        var accept = Find<Button>(window, b => b.Name == "AcceptButton");
        Assert.False(accept.IsEffectivelyEnabled);

        var list = Find<ListBox>(window, _ => true);
        list.SelectedItems!.Add(model.Rows[1]);
        list.SelectedItems.Add(model.Rows[2]);
        Assert.Equal([7u, 8u], model.Selection.Select(s => s.ToplevelId).ToArray());
        Assert.True(accept.IsEffectivelyEnabled);

        Find<CheckBox>(window, c => c.Name == "PersistBox").IsChecked = true;
        Assert.True(model.Persist);
    }

    [AvaloniaFact]
    public void The_source_prompt_selects_with_the_arrow_keys_and_shares_on_Enter()
    {
        var prompt = new SourcePrompt("org.example.App", "", true, PromptSourceKinds.Monitor, false, false,
            [new PromptOutput(null!, "HEADLESS-1", "Headless", new Box(0, 0, 160, 120), 1)], []);
        var model = new SourcePromptModel(in prompt);
        var responses = new List<PromptResponse>();
        model.Completed += responses.Add;
        var window = Show(new SourcePromptView { DataContext = model }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.SourceHeightFor(1));

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Empty(responses);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Single(model.Selection);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal([PromptResponse.Accepted], responses);
    }

    [AvaloniaFact]
    public void The_device_prompt_starts_with_every_requested_device_and_hides_the_rest()
    {
        var prompt = new DevicePrompt("org.example.App", "", true, InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer, true, false);
        var model = new DevicePromptModel(in prompt);
        var window = Show(new DevicePromptView { DataContext = model }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.DeviceHeight);

        Assert.False(Find<CheckBox>(window, c => c.Name == "TouchBox").IsVisible);
        Assert.False(Find<CheckBox>(window, c => c.Name == "PersistBox").IsVisible);
        Assert.True(Find<CheckBox>(window, c => c.Name == "ClipboardBox").IsChecked);
        Find<CheckBox>(window, c => c.Name == "KeyboardBox").IsChecked = false;
        Assert.Equal((InputDeviceCapability.Pointer, true), (model.Devices, model.Clipboard));

        Find<CheckBox>(window, c => c.Name == "PointerBox").IsChecked = false;
        Assert.False(Find<Button>(window, b => b.Name == "AcceptButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void The_area_prompt_turns_a_drag_into_a_box_and_Enter_into_everything()
    {
        var prompt = new AreaPrompt("org.example.App", "", true, null, false);
        var model = new AreaPromptModel(in prompt, 160, 120);
        var window = Show(new AreaPromptView { DataContext = model }, 160, 120);

        window.MouseMove(new Avalonia.Point(20, 30));
        window.MouseDown(new Avalonia.Point(20, 30), MouseButton.Left);
        window.MouseMove(new Avalonia.Point(60, 70), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Avalonia.Point(60, 70), MouseButton.Left);
        Assert.True(model.IsDone);
        Assert.Equal(new Box(20, 30, 40, 40), model.Selection);

        var whole = new AreaPromptModel(in prompt, 160, 120);
        Show(new AreaPromptView { DataContext = whole }, 160, 120).KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(new Box(0, 0, 160, 120), whole.Selection);

        var point = new AreaPromptModel(new AreaPrompt("org.example.App", "", true, null, true), 160, 120);
        var picker = Show(new AreaPromptView { DataContext = point }, 160, 120);
        picker.MouseDown(new Avalonia.Point(11, 12), MouseButton.Left);
        picker.MouseUp(new Avalonia.Point(11, 12), MouseButton.Left);
        Assert.Equal(new Box(11, 12, 1, 1), point.Selection);
    }

    [AvaloniaFact]
    public void The_shortcut_prompt_captures_a_chord_into_the_row_that_was_clicked()
    {
        var prompt = new ShortcutPrompt("org.example.App", "", true,
        [
            new ShortcutPromptRow("record", "Start recording", "CTRL+SHIFT+r", true, ""),
            new ShortcutPromptRow("stop", "Stop recording", "", false, "CTRL+ALT+s"),
        ]);
        var model = new ShortcutPromptModel(in prompt);
        var window = Show(new ShortcutPromptView { DataContext = model }, AvaloniaPortalPrompts.DialogWidth, AvaloniaPortalPrompts.ShortcutHeight);

        Assert.Equal(["", "CTRL+ALT+s"], model.Rows.Select(r => r.Trigger).ToArray());
        Assert.NotNull(Find<TextBlock>(window, t => t.Text == "taken"));

        model.ToggleCapture(model.Rows[1]);
        Assert.Same(model.Rows[1], model.Capturing);
        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Meta | RawInputModifiers.Shift);
        Assert.Null(model.Capturing);
        Assert.Equal("SHIFT+LOGO+F9", model.Rows[1].Trigger);
        Assert.Equal([new ShortcutBinding("stop", "SHIFT+LOGO+F9")], model.Bindings);

        model.ToggleCapture(model.Rows[0]);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Equal("CTRL+a", model.Rows[0].Trigger);
        Assert.Equal(2, model.Bindings.Count);

        model.ToggleCapture(model.Rows[0]);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal("", model.Rows[0].Trigger);
        Assert.Null(model.Capturing);
    }

    [Fact]
    public void Avalonia_keys_map_onto_xkb_keysym_names_and_basin_modifiers()
    {
        Assert.Equal("a", AvaloniaKeys.KeysymNameOf(Key.A));
        Assert.Equal("KP_5", AvaloniaKeys.KeysymNameOf(Key.NumPad5));
        Assert.Equal("F12", AvaloniaKeys.KeysymNameOf(Key.F12));
        Assert.Equal("Print", AvaloniaKeys.KeysymNameOf(Key.PrintScreen));
        Assert.Equal(Modifiers.Ctrl | Modifiers.Super, AvaloniaKeys.ModifiersOf(KeyModifiers.Control | KeyModifiers.Meta));
        Assert.True(AvaloniaKeys.IsModifier(Key.LeftShift));
        Assert.False(AvaloniaKeys.IsModifier(Key.Space));
    }

    private static T Find<T>(Control root, Func<T, bool> predicate)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(predicate);

    private static Window Show(Control content, double width, double height)
    {
        var root = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };

        root.Show();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return root;
    }
}
