using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Westonia.Shell;
using Xunit;

namespace Westonia.Tests;

public sealed class ShellUITests
{
    [AvaloniaFact]
    public void The_panel_puts_its_clock_at_the_far_end_horizontally()
    {
        var model = new PanelModel { ClockDock = Dock.Right, Clock = "12:00" };
        var panel = Layout(new PanelView { DataContext = model }, 800, 32);

        var clock = Find<TextBlock>(panel, t => t.Text == "12:00");
        var bounds = clock.Bounds;
        var position = clock.TranslatePoint(default, panel)!.Value;

        Assert.True(position.X + (bounds.Width / 2) > 400, "the clock sits in the right half");
        Assert.Equal(32, panel.Bounds.Height);
    }

    [AvaloniaFact]
    public void A_vertical_panel_docks_its_clock_at_the_bottom()
    {
        var model = new PanelModel { ClockDock = Dock.Bottom, Clock = "12:00" };
        var panel = Layout(new PanelView { DataContext = model }, 32, 800);

        var clock = Find<TextBlock>(panel, t => t.Text == "12:00");
        var position = clock.TranslatePoint(default, panel)!.Value;

        Assert.True(position.Y + (clock.Bounds.Height / 2) > 400, "the clock sits in the lower half");
    }

    [AvaloniaFact]
    public void A_hidden_clock_takes_no_room()
    {
        var model = new PanelModel { ClockVisible = false, Clock = "12:00" };
        var panel = Layout(new PanelView { DataContext = model }, 800, 32);

        var clock = Find<TextBlock>(panel, t => t.Text == "12:00");

        Assert.False(clock.IsVisible);
        Assert.Equal(0, clock.Bounds.Width);
    }

    [AvaloniaFact]
    public void Every_launcher_gets_a_button_of_its_own_with_its_tooltip()
    {
        var model = new PanelModel();
        model.Launchers.Add(new LauncherModel("Alpha", null, () => { }));
        model.Launchers.Add(new LauncherModel("Bravo", null, () => { }));
        var panel = Layout(new PanelView { DataContext = model }, 800, 32);

        var buttons = panel.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal(2, buttons.Count);
        Assert.Equal("Alpha", ToolTip.GetTip(buttons[0]));
        Assert.Equal("Bravo", ToolTip.GetTip(buttons[1]));
        var first = buttons[0].TranslatePoint(default, panel)!.Value;
        var second = buttons[1].TranslatePoint(default, panel)!.Value;
        Assert.True(first.X + buttons[0].Bounds.Width <= second.X + 1, "launchers lay out in order");
        Assert.Equal(first.Y, second.Y);
    }

    [AvaloniaFact]
    public void A_launcher_button_runs_its_command()
    {
        var ran = 0;
        var model = new PanelModel();
        model.Launchers.Add(new LauncherModel("Alpha", null, () => ran++));
        var panel = Layout(new PanelView { DataContext = model }, 800, 32);

        var button = panel.GetVisualDescendants().OfType<Button>().Single();
        button.Command!.Execute(null);

        Assert.Equal(1, ran);
    }

    [AvaloniaFact]
    public void The_frame_reproduces_westons_metrics()
    {
        var model = new FrameModel { Title = "basin" };
        var outerWidth = 400 + (2 * (FrameModel.Margin + FrameModel.BorderWidth));

        var strip = Layout(
            new FrameTitleView { DataContext = model },
            outerWidth,
            FrameModel.Margin + FrameModel.TitlebarHeight);

        var title = Find<TextBlock>(strip, t => t.Text == "basin");
        var titleTop = title.TranslatePoint(default, strip)!.Value.Y;

        Assert.Equal(32, FrameModel.Margin);
        Assert.Equal(6, FrameModel.BorderWidth);
        Assert.Equal(27, FrameModel.TitlebarHeight);
        Assert.True(titleTop >= FrameModel.Margin, "the title starts below the shadow margin");
        Assert.True(
            titleTop + title.Bounds.Height <= FrameModel.Margin + FrameModel.TitlebarHeight + 1,
            "the title stays inside the titlebar");
        Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Equal(14, title.FontSize);
    }

    [AvaloniaFact]
    public void An_active_frame_and_an_inactive_one_paint_differently()
    {
        var model = new FrameModel { Title = "basin", Active = true };
        var strip = Layout(new FrameTitleView { DataContext = model }, 300, 59);
        var border = strip.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("weston-frame"));

        var active = border.Background;
        model.Active = false;
        strip.UpdateLayout();

        Assert.NotEqual(active, border.Background);
        Assert.IsAssignableFrom<ISolidColorBrush>(border.Background);
    }

    [AvaloniaFact]
    public void The_frame_edges_are_strips_rather_than_a_window_sized_surface()
    {
        var left = Layout(new FrameEdgeView { DataContext = new FrameEdgeModel(FrameEdge.Left) }, 38, 400);
        var bottom = Layout(new FrameEdgeView { DataContext = new FrameEdgeModel(FrameEdge.Bottom) }, 400, 38);

        var leftChrome = left.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("weston-frame"));
        var bottomChrome = bottom.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("weston-frame"));

        Assert.Equal(FrameModel.BorderWidth, leftChrome.Bounds.Width, 0);
        Assert.Equal(FrameModel.BorderWidth, bottomChrome.Bounds.Height, 0);
        Assert.Equal(FrameModel.Margin + FrameModel.BorderWidth, 38);
    }

    [AvaloniaFact]
    public void The_switcher_highlights_one_entry_at_a_time()
    {
        var model = new SwitcherModel();
        model.Entries.Add(new SwitcherEntry("one"));
        model.Entries.Add(new SwitcherEntry("two"));
        model.Entries.Add(new SwitcherEntry("three"));
        model.Entries[1].Selected = true;
        var switcher = Layout(new SwitcherView { DataContext = model }, 320, 120);

        var rows = switcher.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("selected") || b.GetVisualChildren().OfType<TextBlock>().Any())
            .ToList();

        Assert.Equal(3, model.Entries.Count);
        Assert.Single(model.Entries, e => e.Selected);
        Assert.Contains(rows, b => b.Classes.Contains("selected"));
    }

    [AvaloniaFact]
    public void The_unlock_dialog_carries_a_button_that_unlocks()
    {
        var unlocked = 0;
        var dialog = Layout(new UnlockView { DataContext = new UnlockModel(() => unlocked++) }, 640, 480);

        var button = dialog.GetVisualDescendants().OfType<Button>().Single();
        button.Command!.Execute(null);

        Assert.Equal(1, unlocked);
        Assert.Equal("Unlock", button.Content);
    }

    [AvaloniaFact]
    public void Panel_text_follows_the_panel_colour_rather_than_the_session_theme()
    {
        var dark = new PanelModel { Variant = ThemeVariant.Dark, Clock = "12:00" };
        var light = new PanelModel { Variant = ThemeVariant.Light, Clock = "12:00" };

        var onDark = Find<TextBlock>(Layout(new PanelView { DataContext = dark }, 400, 32), t => t.Text == "12:00");
        var onLight = Find<TextBlock>(Layout(new PanelView { DataContext = light }, 400, 32), t => t.Text == "12:00");

        var darkColor = ((ISolidColorBrush)onDark.Foreground!).Color;
        var lightColor = ((ISolidColorBrush)onLight.Foreground!).Color;

        Assert.True(Luminance(darkColor) > 0.5, "dark chrome takes light text");
        Assert.True(Luminance(lightColor) < 0.5, "light chrome takes dark text");
    }

    private static double Luminance(Color color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;

    private static T Find<T>(Control root, Func<T, bool> predicate)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(predicate);

    private static Control Layout(Control content, double width, double height)
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
        return content;
    }
}
