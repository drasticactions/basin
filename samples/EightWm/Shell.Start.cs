using Avalonia.Media;
using AvaWin.Controls;
using Basin;
using Basin.Freedesktop;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private static readonly uint[] AccentPalette =
    [
        0xff2d89ef, 0xff00a4ef, 0xff00aba9, 0xff1e7145, 0xff99b433,
        0xffffc40d, 0xffe3a21a, 0xffda532c, 0xffee1111, 0xffb91d47,
        0xff9f00a7, 0xff7e3878, 0xff603cba, 0xff2b5797,
    ];

    private IconLoader? _icons;
    private Config _config = null!;
    private readonly List<Tile> _tiles = [];
    private readonly List<DesktopEntry> _entries = [];
    private readonly DesktopEntries _desktop = new();

    internal IconLoader Icons => _icons ??= new IconLoader();

    internal Config Configuration => _config;

    internal IReadOnlyList<Tile> Tiles => _tiles;

    internal bool HotCornersOn => _liveHotCorners ?? (Setting("hot_corners") ? _options.HotCorners : _config.HotCorners);

    internal double EdgeBandNow => Setting("edge_band") ? _options.EdgeBand : _config.EdgeBand;

    internal int MinWidthNow => Setting("min_width") ? _options.MinWidth : _config.MinWidth;

    internal int StartOutputNow => Setting("start_output") ? _options.StartOutput : _config.StartOutput;

    private bool Setting(string name) => _options.Explicit.Contains(name);

    private void LoadConfig()
    {
        _config = Config.Load(_options.ConfigPath, _log);
        BuildTiles();
    }

    internal void Reload()
    {
        _config = Config.Load(_options.ConfigPath, _log);
        BuildTiles();
        foreach (var view in Views)
        {
            view.Host.MaxCells = _config.MaxCells;
            view.StartModel.Background = new SolidColorBrush(Color.FromUInt32(_config.Background));
            view.IconScale = 0;
        }

        foreach (var app in _apps)
        {
            ApplyRules(app);
        }

        ClearLiveSettings();
        ApplySettings();
        RelayoutAll();
        _report.Line($"RELOAD tiles={_tiles.Count} rules={_config.Rules.Count}");
    }

    internal void ApplyRules(AppWindow app)
    {
        app.MinWidth = 0;
        foreach (var rule in _config.Rules)
        {
            if (rule.MatchesText(app.AppId, null) && rule.MinWidth > 0)
            {
                app.MinWidth = rule.MinWidth;
                return;
            }
        }
    }

    private void BuildTiles()
    {
        _tiles.Clear();
        _entries.Clear();
        if (_config.ScanDesktopFiles)
        {
            _desktop.Invalidate();
            _entries.AddRange(_desktop.Listable());
        }

        _tiles.AddRange(_config.Tiles);
        if (_tiles.Count == 0)
        {
            var taken = 0;
            foreach (var entry in _entries)
            {
                if (taken >= 24)
                {
                    break;
                }

                if (DesktopLaunch.CommandFor(entry) is not { } command)
                {
                    continue;
                }

                _tiles.Add(new Tile
                {
                    Name = entry.Name,
                    Exec = command,
                    Icon = entry.Icon ?? Path.GetFileNameWithoutExtension(entry.Id),
                    DesktopId = entry.Id,
                    Color = AccentOf(entry.Id),
                    Size = taken % 7 == 0 ? TileSize.Wide : TileSize.Square,
                    Group = taken < 12 ? "Main" : "More",
                });
                taken++;
            }
        }

        foreach (var view in Views)
        {
            view.StartModel.SetTiles(_tiles, _config.GroupOrder);
            view.IconScale = 0;
        }
    }

    private static uint AccentOf(string key)
    {
        var hash = 17u;
        foreach (var character in key)
        {
            hash = (hash * 31) + character;
        }

        return AccentPalette[hash % (uint)AccentPalette.Length];
    }

    private void AttachStart(ShellView view)
    {
        var model = view.StartModel;
        model.Background = new SolidColorBrush(Color.FromUInt32(_config.Background));
        model.SetTiles(_tiles, _config.GroupOrder);
        model.TileInvoked += tile => LaunchTile(view, tile);
        model.AppsRequested += visible =>
        {
            if (_config.AppsView)
            {
                ShowApps(view, visible);
            }
        };
        model.ZoomChanged += zoomedOut => _report.Line($"ZOOM {(zoomedOut ? "out" : "in")}");
        model.AppsSortChanged += sort => _report.Line($"APPS sort={sort}");

        var start = new StartView { DataContext = model };
        start.TileList.SelectionChanged += (_, e) => ReportSelection(e);
        start.TileList.ItemDragDrop += (_, e) => ReportReorder(model, e);
        view.StartView = start;
        view.Start = new AvaloniaChrome(view.StartFrame, _ui, _chromeIndex, start) { Enabled = false };

        var apps = new AppsView { DataContext = model };
        view.AppsView = apps;
        view.AppsSurface = new AvaloniaChrome(view.AppsFrame, _ui, _chromeIndex, apps) { Enabled = false };

        view.Splash = new AvaloniaChrome(
            view.SplashFrame, _ui, _chromeIndex, new SplashView { DataContext = view.SplashModel })
        {
            Enabled = false,
            InputEnabled = false,
        };

        view.FlipFace = new AvaloniaChrome(
            view.FlipFrame, _ui, _chromeIndex, new FlipFaceView { DataContext = view.FlipModel })
        {
            Enabled = false,
            InputEnabled = false,
        };
        view.BackgroundFill = new Basin.Scene.SceneRect(view.Background, 1, 1, DesktopColor);
        view.BackgroundFill.LowerToBottom();
    }

    private static void ReportSelection(Avalonia.Controls.SelectionChangedEventArgs e)
    {
        foreach (var item in e.RemovedItems)
        {
            if (item is Tile tile)
            {
                tile.Selected = false;
                BasinReport.Line($"SELECT {tile.Name} off");
            }
        }

        foreach (var item in e.AddedItems)
        {
            if (item is Tile tile)
            {
                tile.Selected = true;
                BasinReport.Line($"SELECT {tile.Name} on");
            }
        }
    }

    private static void ReportReorder(StartModel model, ListViewDragEventArgs e)
    {
        if (e.Indexes.Count == 0 || e.Items[0] is not Tile tile)
        {
            return;
        }

        var from = e.Indexes[0];
        var first = model.Tiles.IndexOf(model.Tiles.First(other => other.Group == tile.Group));
        var to = e.InsertIndex > from ? e.InsertIndex - 1 : e.InsertIndex;
        if (to != from)
        {
            BasinReport.Line($"REORDER {tile.Name} {from - first}->{to - first}");
        }
    }

    private void PaintStart(ShellView view)
    {
        if (view.Start is not { } start || !view.Background.Enabled)
        {
            return;
        }

        var box = new Box(0, 0, view.Box.Width, view.Box.Height);
        if (view.BackgroundFill is { } fill)
        {
            fill.Color = DesktopColor;
            fill.Width = box.Width;
            fill.Height = box.Height;
        }

        start.Place(box, view.Scale);
        view.AppsSurface?.Place(box, view.Scale);
        start.Enabled = true;
        if (view.AppsSurface is { } apps)
        {
            apps.Enabled = true;
        }

        if (view.IconScale != view.Scale)
        {
            view.IconScale = view.Scale;
            ApplyIcons(view);
        }

        SyncChromeFocus(view);
    }

    internal void SyncChromeFocus(ShellView view)
    {
        var focus = _router.KeyboardFocus;
        if (focus is not null && view.Charms?.PaneSurface is { } pane && ReferenceEquals(focus, pane))
        {
            return;
        }

        var startSurface = view.Start?.Surface;
        var appsSurface = view.AppsSurface?.Surface;
        if (view.Background.Enabled)
        {
            var wanted = view.AppsVisible ? appsSurface : startSurface;
            if (wanted is not null)
            {
                _router.SetKeyboardFocus(wanted);
            }
        }
        else if (focus is not null && (ReferenceEquals(focus, startSurface) || ReferenceEquals(focus, appsSurface)))
        {
            _router.SetKeyboardFocus(null);
        }
    }

    private void ApplyIcons(ShellView view)
    {
        foreach (var tile in view.StartModel.Tiles)
        {
            tile.IconImage = tile.Icon is { Length: > 0 } icon
                ? Icons.Load(icon, (int)Math.Round(tile.IconSize * view.Scale))
                : null;
        }

        foreach (var app in view.StartModel.Apps)
        {
            app.IconImage = app.Icon is { Length: > 0 } icon
                ? Icons.Load(icon, (int)Math.Round(AppIconSize * view.Scale))
                : null;
        }
    }

    internal const double AppIconSize = 30;

    private readonly Dictionary<string, int> _launches = [];

    private static DateTime InstalledAt(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private List<Tile> AppTiles()
    {
        var apps = new List<Tile>();
        foreach (var group in DesktopCategories.Group(_entries))
        {
            var label = DesktopCategories.DefaultLabel(group.Category);
            foreach (var entry in group.Entries)
            {
                if (DesktopLaunch.CommandFor(entry) is not { } command)
                {
                    continue;
                }

                apps.Add(new Tile
                {
                    Name = entry.Name,
                    Exec = command,
                    Icon = entry.Icon ?? Path.GetFileNameWithoutExtension(entry.Id),
                    DesktopId = entry.Id,
                    Size = TileSize.Small,
                    Color = AccentOf(entry.Id),
                    Group = label,
                    Installed = InstalledAt(entry.Path),
                    Launches = _launches.GetValueOrDefault(command),
                });
            }
        }

        return apps;
    }

    internal void ShowApps(ShellView view, bool visible)
    {
        if (view.Start is null || view.AppsVisible == visible)
        {
            return;
        }

        if (visible && view.StartModel.Apps.Count == 0)
        {
            view.StartModel.SetApps(AppTiles());
            view.IconScale = 0;
        }

        view.AppsVisible = visible;
        view.AppsFrame.Enabled = true;
        view.StartFrame.Enabled = true;
        double height = view.Box.Height;
        var startFrom = view.StartPageMotion.IsRunning ? view.StartPageMotion.Offset : visible ? 0 : -height;
        var appsFrom = view.AppsMotion.IsRunning ? view.AppsMotion.Offset : visible ? height : 0;
        Animate(ref view.StartPageMotion, view.StartFrame, PageSlide(startFrom, visible ? -height : 0));
        Animate(ref view.AppsMotion, view.AppsFrame, PageSlide(appsFrom, visible ? 0 : height));
        if (!AnimationsOn)
        {
            view.AppsFrame.Enabled = visible;
        }

        SyncChromeFocus(view);
        _outputs.RepaintNow(view.Driver);
        _report.Line($"APPS {(visible ? "on" : "off")}");
    }

    internal const uint PageSlideMillis = 550;

    private static AnimationSpec PageSlide(double from, double to) => new(
        Animation.EnterPage, MotionAxis.Y,
        new Track(from, to, PageSlideMillis, 0, AnimationCurve.Deceleration), Track.None, Track.None, 0, 0);

    internal void ToggleZoom(ShellView view, bool zoomOut) => view.StartModel.ZoomedOut = zoomOut;

    private readonly List<(Tile Tile, System.Diagnostics.Process Process, bool IsBadge)> _polls = [];

    internal void PollTiles()
    {
        var now = Environment.TickCount64;
        for (var i = _polls.Count - 1; i >= 0; i--)
        {
            var (tile, process, isBadge) = _polls[i];
            if (!process.HasExited)
            {
                continue;
            }

            _polls.RemoveAt(i);
            string output;
            try
            {
                output = process.StandardOutput.ReadToEnd().Trim();
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
            {
                output = string.Empty;
            }

            if (process.ExitCode != 0 || output.Length == 0)
            {
                _log.Debug($"tile {tile.Name} poll gave nothing");
                process.Dispose();
                continue;
            }

            process.Dispose();
            var line = output.Split('\n')[0].Trim();
            if (isBadge)
            {
                tile.Badge = line;
            }
            else
            {
                tile.Peek = line;
            }
        }

        foreach (var tile in _tiles)
        {
            if (tile.PeekCommand is null && tile.BadgeCommand is null)
            {
                continue;
            }

            if (now < tile.NextPollMillis)
            {
                continue;
            }

            tile.NextPollMillis = now + (Math.Max(1, tile.PeekIntervalSeconds) * 1000L);
            StartPoll(tile, tile.PeekCommand, isBadge: false);
            StartPoll(tile, tile.BadgeCommand, isBadge: true);
        }
    }

    private void StartPoll(Tile tile, string? command, bool isBadge)
    {
        if (command is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
            if (System.Diagnostics.Process.Start(info) is { } process)
            {
                _polls.Add((tile, process, isBadge));
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Debug($"tile {tile.Name} cannot run '{command}': {error.Message}");
        }
    }

    private void StopPolls()
    {
        foreach (var (_, process, _) in _polls)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            {
            }

            process.Dispose();
        }

        _polls.Clear();
    }

    internal const long SplashTimeoutMillis = 10_000;

    internal void ShowSplash(ShellView view, string title, uint color, in Box box)
    {
        if (!PrepareSplash(view, title, color, box))
        {
            return;
        }

        Animate(ref view.SplashMotion, view.SplashFrame, Animation.FadeIn);
    }

    private static bool PrepareSplash(ShellView view, string title, uint color, in Box box)
    {
        if (view.Splash is not { } splash)
        {
            return false;
        }

        view.SplashModel.Title = title;
        view.SplashModel.Fill = new Avalonia.Media.SolidColorBrush(color);
        view.SplashDeadlineMillis = Environment.TickCount64 + SplashTimeoutMillis;
        view.SplashBox = box;
        splash.Enabled = true;
        view.SplashFrame.Enabled = true;
        view.SplashMotion.Stop();
        Tween.Reset(view.SplashFrame);
        PaintSplash(view);
        return true;
    }

    private static void PaintSplash(ShellView view)
    {
        if (view.Splash is { Enabled: true } splash)
        {
            var box = view.SplashBox.IsEmpty ? new Box(0, 0, view.Box.Width, view.Box.Height) : view.SplashBox;
            splash.Place(box, view.Scale);
        }
    }

    internal void DismissSplash(ShellView view, bool crossFade)
    {
        if (view.Splash is not { Enabled: true } splash)
        {
            return;
        }

        if (!crossFade || !AnimationsOn)
        {
            splash.Enabled = false;
            Tween.Reset(view.SplashFrame);
            return;
        }

        view.SplashMotion.Start(AnimationCatalog.Of(Animation.CrossFadeOut), _clockMillis);
        view.SplashMotion.Apply(view.SplashFrame);
    }

    private void ExpireSplashes()
    {
        var now = Environment.TickCount64;
        foreach (var view in Views)
        {
            if (view.Splash is { Enabled: true } && now >= view.SplashDeadlineMillis)
            {
                _report.Line($"SPLASH timeout {view.SplashModel.Title}");
                DismissSplash(view, crossFade: false);
                RestorePage(view);
            }
        }
    }

    private void AdvanceSplash(ShellView view, long nowMillis)
    {
        if (view.Splash is not { Enabled: true } splash || !view.SplashMotion.IsRunning)
        {
            return;
        }

        view.SplashMotion.Advance(nowMillis);
        view.SplashMotion.Apply(view.SplashFrame);
        if (!view.SplashMotion.IsRunning && view.SplashMotion.Name == Animation.CrossFadeOut)
        {
            splash.Enabled = false;
            Tween.Reset(view.SplashFrame);
        }
    }

    private void ReleaseChrome()
    {
        StopPolls();
        foreach (var view in Views)
        {
            view.ReleaseChrome();
        }

        _icons?.Dispose();
        _icons = null;
    }
}
