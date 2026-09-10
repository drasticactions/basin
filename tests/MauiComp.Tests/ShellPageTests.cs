using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using MauiComp.Shell;
using Microsoft.Maui;
using Xunit;
using M = Microsoft.Maui.Controls;

namespace MauiComp.Tests;

public sealed class ShellPageTests
{
    [AvaloniaFact]
    public void The_panel_puts_its_clock_at_the_far_right()
    {
        var model = new PanelModel { Clock = "12:00" };
        var panel = MauiPages.Layout(new PanelPage { BindingContext = model }, 800, ShellOutputs.PanelThickness);

        var clock = Find<TextBlock>(panel, t => t.Text == "12:00");
        var position = clock.TranslatePoint(default, panel)!.Value;

        Assert.True(position.X + (clock.Bounds.Width / 2) > 400, "the clock sits in the right half");
        Assert.Equal(ShellOutputs.PanelThickness, panel.Bounds.Height);
    }

    [AvaloniaFact]
    public void Every_mapped_window_gets_a_task_button_that_activates_it()
    {
        var activated = new List<string>();
        var model = new PanelModel();
        model.Tasks.Add(new TaskEntry("alpha", () => activated.Add("alpha")));
        model.Tasks.Add(new TaskEntry("bravo", () => activated.Add("bravo")) { Active = true });
        var page = new PanelPage { BindingContext = model };
        var panel = MauiPages.Layout(page, 800, ShellOutputs.PanelThickness);

        var alpha = Find<TextBlock>(panel, t => t.Text == "alpha");
        var bravo = Find<TextBlock>(panel, t => t.Text == "bravo");
        Assert.True(
            alpha.TranslatePoint(default, panel)!.Value.X < bravo.TranslatePoint(default, panel)!.Value.X,
            "tasks lay out in order");

        var taps = MauiPages.Descendants<M.Border>(page)
            .Select(b => b.GestureRecognizers.OfType<M.TapGestureRecognizer>().FirstOrDefault())
            .Where(t => t?.Command is not null)
            .ToList();
        Assert.Equal(2, taps.Count);
        taps[0]!.Command!.Execute(null);

        Assert.Equal(["alpha"], activated);
    }

    [AvaloniaFact]
    public void A_task_entry_changes_its_colour_when_it_becomes_active()
    {
        var model = new PanelModel();
        var entry = new TaskEntry("alpha", () => { });
        model.Tasks.Add(entry);
        var page = new PanelPage { BindingContext = model };
        MauiPages.Layout(page, 800, ShellOutputs.PanelThickness);
        var button = MauiPages.Descendants<M.Border>(page)
            .First(b => b.GestureRecognizers.OfType<M.TapGestureRecognizer>().Any(t => t.Command is not null));

        var inactive = button.Background;
        entry.Active = true;
        var active = button.Background;

        Assert.NotEqual(inactive, active);
    }

    [AvaloniaFact]
    public void The_titlebar_carries_its_title_and_three_buttons_that_route_to_the_window()
    {
        var actions = new List<string>();
        var model = new TitlebarModel(() => actions.Add("close"), () => actions.Add("maximize"), () => actions.Add("minimize"))
        {
            Title = "basin",
        };
        var page = new TitlebarPage { BindingContext = model };
        var strip = MauiPages.Layout(page, 400, ShellTitlebar.Height);

        var title = Find<TextBlock>(strip, t => t.Text == "basin");
        Assert.Equal(Avalonia.Media.FontWeight.Bold, title.FontWeight);
        var buttons = MauiPages.Descendants<M.Border>(page)
            .Where(b => b.GestureRecognizers.OfType<M.TapGestureRecognizer>().Any(t => t.Command is not null))
            .ToList();
        Assert.Equal(3, buttons.Count);
        foreach (var button in buttons)
        {
            button.GestureRecognizers.OfType<M.TapGestureRecognizer>().Single().Command!.Execute(null);
        }

        Assert.Equal(["minimize", "maximize", "close"], actions);
        Assert.All(buttons, b => Assert.Equal(21, b.WidthRequest));
        var positions = buttons
            .Select(b => ((Control)b.Handler!.PlatformView!).TranslatePoint(default, strip)!.Value.X)
            .ToList();
        Assert.True(positions[0] < positions[1] && positions[1] < positions[2], "the buttons keep their order");
        Assert.True(positions[0] > title.TranslatePoint(default, strip)!.Value.X, "the title sits left of the buttons");
    }

    [AvaloniaFact]
    public void An_active_titlebar_and_an_inactive_one_paint_differently()
    {
        var model = new TitlebarModel(() => { }, () => { }, () => { }) { Title = "basin", Active = true };
        var page = new TitlebarPage { BindingContext = model };
        MauiPages.Layout(page, 400, ShellTitlebar.Height);

        var caption = MauiPages.Descendants<M.Border>(page).First();
        var active = caption.Background;
        model.Active = false;
        var inactive = caption.Background;

        Assert.NotNull(active);
        Assert.NotEqual(active, inactive);
    }

    [AvaloniaFact]
    public void The_start_menu_lists_programs_run_and_exit_in_order()
    {
        var actions = new List<string>();
        var model = new StartMenuModel(() => actions.Add("programs"), () => actions.Add("run"), () => actions.Add("exit"));
        var page = new StartMenuPage { BindingContext = model };
        var menu = MauiPages.Layout(page, ShellStartMenu.Width, ShellStartMenu.Height);

        var labels = new[] { "Programs", "Run...", "Exit" }.Select(text => Find<TextBlock>(menu, t => t.Text == text)).ToList();
        var tops = labels.Select(l => l.TranslatePoint(default, menu)!.Value.Y).ToList();
        Assert.True(tops[0] < tops[1] && tops[1] < tops[2], "the rows keep the classic order");
        Assert.True(labels.All(l => l.TranslatePoint(default, menu)!.Value.X > 24), "every row sits right of the banner");

        var rows = MauiPages.Descendants<M.Border>(page)
            .Where(b => b.GestureRecognizers.OfType<M.TapGestureRecognizer>().Any(t => t.Command is not null))
            .ToList();
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            row.GestureRecognizers.OfType<M.TapGestureRecognizer>().Single().Command!.Execute(null);
        }

        Assert.Equal(["programs", "run", "exit"], actions);
    }

    [AvaloniaFact]
    public void The_run_prompt_hands_over_what_was_typed_and_can_be_cancelled()
    {
        var ran = new List<string>();
        var cancelled = 0;
        var model = new RunModel(ran.Add, () => cancelled++);
        var page = new RunPage { BindingContext = model };
        var dialog = MauiPages.Layout(page, ShellRunDialog.Width, ShellRunDialog.Height);

        Assert.NotNull(Find<TextBlock>(dialog, t => t.Text == "Run"));
        var entry = MauiPages.Descendants<M.Entry>(page).Single();
        entry.Text = "weston-flower";
        var buttons = MauiPages.Descendants<M.Border>(page)
            .Where(b => b.GestureRecognizers.OfType<M.TapGestureRecognizer>().Any(t => t.Command is not null))
            .ToList();
        Assert.Equal(3, buttons.Count);
        foreach (var button in buttons)
        {
            button.GestureRecognizers.OfType<M.TapGestureRecognizer>().Single().Command!.Execute(null);
        }

        Assert.Equal(["weston-flower"], ran);
        Assert.Equal(2, cancelled);
    }

    [AvaloniaFact]
    public void The_switcher_highlights_one_entry_at_a_time()
    {
        var model = new SwitcherModel();
        model.Entries.Add(new SwitcherEntry("one"));
        model.Entries.Add(new SwitcherEntry("two"));
        model.Entries.Add(new SwitcherEntry("three"));
        model.Entries[1].Selected = true;
        var switcher = MauiPages.Layout(new SwitcherPage { BindingContext = model }, 320, 106);

        var texts = switcher.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Equal(["one", "two", "three"], texts);
        Assert.Single(model.Entries, e => e.Selected);
    }

    [AvaloniaFact]
    public void A_window_scope_is_its_own_and_tears_down_with_its_page()
    {
        var app = MauiPages.App;
        var before = app.Application.Windows.Count;

        var (first, firstWindow, firstScope) = MauiPages.Realize(new BackgroundPage { BindingContext = new BackgroundModel() });
        var (second, secondWindow, secondScope) = MauiPages.Realize(new BackgroundPage { BindingContext = new BackgroundModel() });

        Assert.NotSame(first, second);
        Assert.NotSame(firstWindow.Handler!.MauiContext, secondWindow.Handler!.MauiContext);
        Assert.Equal(before + 2, app.Application.Windows.Count);

        ((IWindow)firstWindow).Destroying();
        firstScope.Dispose();
        Assert.Equal(before + 1, app.Application.Windows.Count);
        Assert.Null(firstWindow.Handler);

        ((IWindow)secondWindow).Destroying();
        secondScope.Dispose();
        Assert.Equal(before, app.Application.Windows.Count);
    }

    private static T Find<T>(Control root, Func<T, bool> predicate)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(predicate);
}
