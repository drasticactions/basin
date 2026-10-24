using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaUnion;
using TabWm.Chrome;
using Xunit;

namespace TabWm.Tests;

public sealed class StripViewTests
{
    private static (Window Window, StripView View, StripModel Model) Strip(int tabs, double width = 900)
    {
        var model = new StripModel();
        for (var i = 0; i < tabs; i++)
        {
            model.Insert(i, new TabModel((ulong)(i + 1)) { Label = $"Tab {i + 1}" });
        }

        model.Select(0);
        model.Reordered = (tab, index) => model.Move(tab, index);
        var view = new StripView { DataContext = model };
        var window = ViewHarness.Show(view, width, 40);
        return (window, view, model);
    }

    private static TabItem Item(StripView view, int index) => (TabItem)view.TabControl.ContainerFromIndex(index)!;

    private static Point Center(Visual relativeTo, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    [AvaloniaFact]
    public void The_strip_keeps_its_height_when_a_tab_shows_or_hides_its_badges()
    {
        var (_, view, model) = Strip(2);
        ViewHarness.Pump();
        var item = Item(view, 0);
        var bare = (Item: item.Bounds.Height, Strip: view.DesiredSize.Height);

        var tab = model.Tabs[0];
        foreach (var (name, set, clear) in new (string, Action, Action)[]
        {
            ("speaker", () => tab.Audio = TabAudioState.Playing, () => tab.Audio = TabAudioState.None),
            ("muted speaker", () => tab.Audio = TabAudioState.Muted, () => tab.Audio = TabAudioState.None),
            ("attention dot", () => tab.Attention = true, () => tab.Attention = false),
            ("busy spinner", () => tab.Busy = true, () => tab.Busy = false),
        })
        {
            set();
            ViewHarness.Pump();
            Assert.True(bare.Item == item.Bounds.Height, $"the {name} changed the tab height from {bare.Item} to {item.Bounds.Height}");
            Assert.True(bare.Strip == view.DesiredSize.Height, $"the {name} changed the strip height from {bare.Strip} to {view.DesiredSize.Height}");
            clear();
            ViewHarness.Pump();
            Assert.Equal(bare.Item, item.Bounds.Height);
        }
    }

    [AvaloniaFact]
    public void A_tab_shows_its_label_icon_speaker_and_attention_dot()
    {
        var (_, view, model) = Strip(1);
        var tab = model.Tabs[0];
        tab.Label = "Mail — Inbox";
        tab.IconName = "mail-message";
        tab.Audio = TabAudioState.Playing;
        tab.Attention = true;
        ViewHarness.Pump();

        var item = Item(view, 0);
        var label = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("tab-label"));
        Assert.Equal("Mail — Inbox", label.Text);
        Assert.True(item.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab-audio")).IsVisible);
        Assert.True(item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Classes.Contains("tab-attention")).IsVisible);
        Assert.Equal("audio-volume-high", tab.AudioIcon);

        tab.Audio = TabAudioState.Muted;
        tab.Attention = false;
        ViewHarness.Pump();
        Assert.Equal("audio-volume-muted", tab.AudioIcon);
        Assert.False(item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Classes.Contains("tab-attention")).IsVisible);

        tab.Audio = TabAudioState.None;
        ViewHarness.Pump();
        Assert.False(item.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab-audio")).IsVisible);
    }

    [AvaloniaFact]
    public void A_middle_click_raises_the_close_request()
    {
        var (window, view, model) = Strip(2);
        var closed = new List<TabModel>();
        model.CloseRequested = closed.Add;
        var point = Center(window, Item(view, 1));
        window.MouseDown(point, MouseButton.Middle);
        window.MouseUp(point, MouseButton.Middle);
        ViewHarness.Pump();
        Assert.Equal([model.Tabs[1]], closed);
    }

    [AvaloniaFact]
    public void A_drag_reorders_the_tabs_and_refreshes_the_first_and_last_hints()
    {
        var (window, view, model) = Strip(3);
        var first = model.Tabs[0];
        var from = Center(window, Item(view, 0));
        var to = Center(window, Item(view, 2));
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 10, from.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(["Tab 2", "Tab 3", "Tab 1"], model.Tabs.Select(t => t.Label));
        Assert.Same(first, model.Tabs[2]);
        Assert.Contains("first", Union.GetNode(Item(view, 0))!.Hints);
        Assert.DoesNotContain("first", Union.GetNode(Item(view, 2))!.Hints);
        Assert.Contains("last", Union.GetNode(Item(view, 2))!.Hints);
    }

    [AvaloniaFact]
    public void Leaving_the_strip_vertically_hands_the_tab_to_the_compositor()
    {
        var (window, view, model) = Strip(2);
        TabModel? handed = null;
        model.DragOut = tab =>
        {
            handed = tab;
            return true;
        };
        var from = Center(window, Item(view, 1));
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X, from.Y + 10), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(from.X, from.Y + 80), RawInputModifiers.LeftMouseButton);
        ViewHarness.Pump();
        Assert.Same(model.Tabs[1], handed);
        window.MouseUp(new Point(from.X, from.Y + 80), MouseButton.Left);
    }

    [AvaloniaFact]
    public void The_new_tab_button_stays_visible_when_the_tabs_overflow()
    {
        var (window, view, model) = Strip(30, width: 700);
        var plus = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewTabButton");
        var status = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "Status");
        var plusBox = plus.TranslatePoint(default, window)!.Value;
        var statusBox = status.TranslatePoint(default, window)!.Value;
        Assert.True(plus.IsEffectivelyVisible);
        Assert.True(plusBox.X + plus.Bounds.Width <= statusBox.X + 0.5, $"the + ends at {plusBox.X + plus.Bounds.Width}, the status starts at {statusBox.X}");
        Assert.True(plusBox.X >= 0 && plusBox.X + plus.Bounds.Width <= 700);

        var (fewWindow, fewView, _) = Strip(2, width: 700);
        var fewPlus = fewView.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewTabButton");
        var lastTab = Item(fewView, 1);
        var tabEnd = lastTab.TranslatePoint(new Point(lastTab.Bounds.Width, 0), fewWindow)!.Value.X;
        Assert.True(fewPlus.TranslatePoint(default, fewWindow)!.Value.X - tabEnd < 40, "the + sits next to the last tab while the tabs fit");
    }

    [AvaloniaFact]
    public void A_double_click_renames_and_escape_clears_the_rename()
    {
        var (window, view, model) = Strip(1);
        var renames = new List<string?>();
        model.Renamed = (_, label) => renames.Add(label);
        var tab = model.Tabs[0];
        model.BeginRename(tab);
        Assert.True(tab.IsEditing);
        tab.EditText = "Work";
        model.CommitRename(tab, keep: true);
        model.BeginRename(tab);
        model.CommitRename(tab, keep: false);
        Assert.Equal(["Work", null], renames);
        Assert.False(tab.IsEditing);
        _ = window;
        _ = view;
    }
}
