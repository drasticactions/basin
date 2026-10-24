using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabWm.Chrome;
using Xunit;

namespace TabWm.Tests;

public sealed class LauncherViewTests
{
    private static readonly string[] Names = ["Foot", "Firefox", "Files", "Konsole"];

    private static (Window Window, LauncherView View, LauncherViewModel Model, List<string> Launched) Launcher()
    {
        var launched = new List<string>();
        var model = new LauncherViewModel();
        model.Searched = text => model.SetResults(Names
            .Where(name => name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .Select(name => new LauncherEntryModel(name.ToLowerInvariant() + ".desktop", name, "application-x-executable", null)));
        model.Launch = entry => launched.Add(entry.Id);
        model.Run = text => launched.Add("run:" + text);
        model.SetPinned([new LauncherEntryModel("foot.desktop", "Foot", "utilities-terminal", null)]);
        var view = new LauncherView { DataContext = model };
        var window = ViewHarness.Show(view, 800, 600);
        view.FocusSearch();
        ViewHarness.Pump();
        return (window, view, model, launched);
    }

    [AvaloniaFact]
    public void Typing_filters_the_list_and_hides_the_pinned_row()
    {
        var (window, view, model, _) = Launcher();
        window.KeyTextInput("f");
        ViewHarness.Pump();
        Assert.Equal("f", model.SearchText);
        Assert.Equal(["Foot", "Firefox", "Files"], model.Results.Select(r => r.Name));
        Assert.False(view.FindControl<ListBox>("Pinned")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<ListBox>("Results")!.IsEffectivelyVisible);
        window.KeyTextInput("i");
        ViewHarness.Pump();
        Assert.Equal(["Firefox", "Files"], model.Results.Select(r => r.Name));
    }

    [AvaloniaFact]
    public void The_arrow_keys_move_the_selection_and_enter_launches_it()
    {
        var (window, _, model, launched) = Launcher();
        window.KeyTextInput("f");
        ViewHarness.Pump();
        Assert.Equal(0, model.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(1, model.SelectedIndex);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(["firefox.desktop"], launched);
    }

    [AvaloniaFact]
    public void Text_that_matches_nothing_runs_as_a_command()
    {
        var (window, _, model, launched) = Launcher();
        model.Searched = text => model.SetResults([new LauncherEntryModel("run", $"Run “{text}”", "system-run", null, run: true)]);
        window.KeyTextInput("htop");
        ViewHarness.Pump();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(["run:htop"], launched);
    }

    [AvaloniaFact]
    public void Escape_clears_the_search_then_dismisses()
    {
        var (window, _, model, _) = Launcher();
        var dismissed = 0;
        model.Dismiss = () => dismissed++;
        window.KeyTextInput("fo");
        ViewHarness.Pump();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(string.Empty, model.SearchText);
        Assert.Equal(0, dismissed);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal(1, dismissed);
    }

    [AvaloniaFact]
    public void A_click_on_a_pinned_tile_launches_it()
    {
        var (window, view, _, launched) = Launcher();
        var pinned = view.FindControl<ListBox>("Pinned")!;
        var tile = (Control)pinned.ContainerFromIndex(0)!;
        var point = tile.TranslatePoint(new Avalonia.Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(["foot.desktop"], launched);
        _ = view.GetVisualDescendants();
    }
}
