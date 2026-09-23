using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaWin.Controls;
using Xunit;

namespace EightWm.Tests;

public sealed class ViewTests
{
    private static Window Host(Control content, double width = 1366, double height = 768)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        window.UpdateLayout();
        return window;
    }

    private static Tile NewTile(string name, TileSize size, string group = "Main") =>
        new() { Name = name, Exec = "true", Size = size, Group = group };

    private static StartModel DefaultStart()
    {
        var model = new StartModel();
        model.SetTiles(
            [
                NewTile("Wide", TileSize.Wide),
                NewTile("Square", TileSize.Square),
                NewTile("Small", TileSize.Small),
                NewTile("Large", TileSize.Large, "More"),
                NewTile("Other", TileSize.Square, "More"),
            ],
            []);
        return model;
    }

    private static Rect BoxOf(StartView view, int index)
    {
        var container = view.TileList.ContainerFromIndex(index)!;
        var origin = container.TranslatePoint(default, view)!.Value;
        return new Rect(origin, container.Bounds.Size);
    }

    [AvaloniaFact]
    public void Start_lays_out_the_windows_8_tile_sizes_with_a_ten_pixel_gap()
    {
        var view = new StartView { DataContext = DefaultStart() };
        var window = Host(view);

        var wide = BoxOf(view, 0);
        var square = BoxOf(view, 1);
        var small = BoxOf(view, 2);
        var large = BoxOf(view, 3);
        Assert.Equal(new Size(310, 150), wide.Size);
        Assert.Equal(new Size(150, 150), square.Size);
        Assert.Equal(new Size(70, 70), small.Size);
        Assert.Equal(new Size(310, 310), large.Size);
        Assert.Equal(10, square.Top - wide.Bottom, 0.5);
        Assert.Equal(wide.Left, square.Left, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void Groups_sit_side_by_side()
    {
        var view = new StartView { DataContext = DefaultStart() };
        var window = Host(view);

        var main = new[] { 0, 1, 2 }.Select(i => BoxOf(view, i)).ToArray();
        var more = BoxOf(view, 3);
        Assert.True(more.Left > main.Max(box => box.Right));
        Assert.Equal(main[0].Top, more.Top, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void The_zoomed_out_view_lists_every_group()
    {
        var model = DefaultStart();
        var view = new StartView { DataContext = model };
        var window = Host(view);

        model.ZoomedOut = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.SemanticZoom.IsZoomedOut);
        Assert.Equal(["Main", "More"], view.GroupsList.Items.Cast<TileGroupModel>().Select(group => group.Name));
        Assert.Equal([3, 2], view.GroupsList.Items.Cast<TileGroupModel>().Select(group => group.Count));
        window.Close();
    }

    [AvaloniaFact]
    public void The_charms_bar_has_five_charms_96_pixels_apart()
    {
        var activated = new List<string>();
        var view = new CharmsBarView { DataContext = new CharmsModel(activated.Add) };
        var window = Host(view, 88, 768);

        var charms = view.Charms.ToArray();
        Assert.Equal(5, charms.Length);
        var tops = charms.Select(charm => charm.TranslatePoint(default, view)!.Value.Y).ToArray();
        for (var i = 1; i < tops.Length; i++)
        {
            Assert.Equal(96, tops[i] - tops[i - 1], 0.5);
        }

        Assert.Equal(768 / 2.0, (tops[0] + tops[4] + 96) / 2, 0.5);
        charms[4].Command!.Execute(charms[4].CommandParameter);
        Assert.Equal(["Settings"], activated);
        window.Close();
    }

    [AvaloniaFact]
    public void The_settings_pane_binds_its_controls_to_the_model()
    {
        var model = new CharmPaneModel { Title = "Settings", IsSettings = true };
        model.Settings.SetPalette([0xff2d89ef, 0xffee1111]);
        model.Settings.Load(dark: true, accent: 0xff2d89ef, animations: true, hotCorners: true);
        var changed = new List<string>();
        model.Settings.Changed += changed.Add;
        var view = new CharmPaneView { DataContext = model };
        var window = Host(view, 345, 768);

        Assert.True(view.ThemeSwitch.IsChecked);
        view.ThemeSwitch.IsChecked = false;
        Assert.False(model.Settings.Dark);
        view.AnimationsSwitch.IsChecked = false;
        view.HotCornersSwitch.IsChecked = false;
        Assert.False(model.Settings.Animations);
        Assert.False(model.Settings.HotCorners);
        Assert.Equal(2, view.AccentList.ItemCount);
        model.Settings.Accents[1].ChooseCommand.Execute(null);
        Assert.Equal(0xffee1111, model.Settings.Accent);
        Assert.True(model.Settings.Accents[1].IsCurrent);
        Assert.False(model.Settings.Accents[0].IsCurrent);
        Assert.Equal(["Dark", "Animations", "HotCorners", "Accent"], changed);
        Assert.True(view.SettingsPanel.IsVisible);

        model.IsSettings = false;
        Assert.False(view.SettingsPanel.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_titlebar_close_button_raises_the_close_command()
    {
        var closed = 0;
        var view = new TitleBarView { DataContext = new TitleModel(() => closed++) { Title = "simple-shm" } };
        var window = Host(view, 800, 48);

        var close = view.CloseButton;
        var centre = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
        Assert.True(view.IsOverClose(centre.X, centre.Y));
        Assert.False(view.IsOverClose(20, 24));
        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Assert.Equal(1, closed);
        window.Close();
    }

    [AvaloniaFact]
    public void The_theme_flips_without_rebuilding_the_pane()
    {
        var model = new CharmPaneModel { Title = "Settings", IsSettings = true };
        var view = new CharmPaneView { DataContext = model };
        var window = Host(view, 345, 768);
        var presenter = view.GetVisualDescendants().OfType<SettingsFlyoutPresenter>().Single();

        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = ((ISolidColorBrush)presenter.Background!).Color;
        app.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var light = ((ISolidColorBrush)presenter.Background!).Color;

        Assert.NotEqual(dark, light);
        Assert.Same(presenter, view.GetVisualDescendants().OfType<SettingsFlyoutPresenter>().Single());
        app.RequestedThemeVariant = ThemeVariant.Default;
        window.Close();
    }

    [AvaloniaFact]
    public void The_apps_filter_narrows_the_list()
    {
        var model = new StartModel();
        model.SetApps(
        [
            NewTile("Firefox", TileSize.Small, "Internet"),
            NewTile("Files", TileSize.Small, "System"),
            NewTile("Terminal", TileSize.Small, "System"),
        ]);
        var view = new AppsView { DataContext = model };
        var window = Host(view);
        Assert.Equal(3, view.List.ItemCount);

        model.Filter = "fi";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Files", "Firefox"], view.List.Items.Cast<Tile>().Select(tile => tile.Name));

        model.Filter = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, view.List.ItemCount);
        window.Close();
    }

    [AvaloniaFact]
    public void The_apps_list_sorts_like_windows_8_1()
    {
        var model = new StartModel();
        model.SetApps(
        [
            new Tile { Name = "Terminal", Exec = "t", Group = "System", Installed = new DateTime(2026, 1, 3) },
            new Tile { Name = "Firefox", Exec = "f", Group = "Internet", Installed = new DateTime(2026, 1, 1), Launches = 5 },
            new Tile { Name = "Files", Exec = "e", Group = "System", Installed = new DateTime(2026, 1, 2), Launches = 2 },
        ]);
        var view = new AppsView { DataContext = model };
        var window = Host(view);
        string[] Names() => view.List.Items.Cast<Tile>().Select(tile => tile.Name).ToArray();

        Assert.Equal(["Files", "Firefox", "Terminal"], Names());
        Assert.Null(view.List.Groups);

        model.AppsSort = AppsSort.DateInstalled;
        Assert.Equal(["Terminal", "Files", "Firefox"], Names());
        model.AppsSort = AppsSort.MostUsed;
        Assert.Equal(["Firefox", "Files", "Terminal"], Names());
        model.AppsSort = AppsSort.Category;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Terminal", "Firefox", "Files"], Names());
        Assert.Equal(["System", "Internet", "System"], view.List.Groups!.Select(group => (string)group.Key!));
        Assert.Equal("by category", model.AppsSortLabel);
        window.Close();
    }

    [AvaloniaFact]
    public void The_arrows_move_between_start_and_apps()
    {
        var model = DefaultStart();
        var requests = new List<bool>();
        model.AppsRequested += requests.Add;
        var start = new StartView { DataContext = model };
        var window = Host(start);
        var down = start.AppsArrow.TranslatePoint(new Point(20, 20), window)!.Value;
        Assert.True(down.X < 100 && down.Y > 700);
        window.MouseDown(down, Avalonia.Input.MouseButton.Left);
        window.MouseUp(down, Avalonia.Input.MouseButton.Left);
        window.Close();

        var apps = new AppsView { DataContext = model };
        window = Host(apps);
        var up = apps.StartArrow.TranslatePoint(new Point(20, 20), window)!.Value;
        Assert.Equal(down, up);
        window.MouseDown(up, Avalonia.Input.MouseButton.Left);
        window.MouseUp(up, Avalonia.Input.MouseButton.Left);
        Assert.Equal([true, false], requests);
        window.Close();
    }
}
