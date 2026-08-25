using Basin;
using Basin.Capabilities;
using Basin.Render.Skia;
using Basin.Scene;
using Basin.UI.Skia;
using SkiaSharp;

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

    private IUIHost? _uiHost;
    private IconLoader? _icons;
    private Config _config = null!;
    private readonly List<Tile> _tiles = [];
    private readonly List<DesktopEntry> _entries = [];

    internal IUIHost UIHost => _uiHost ??= SkiaUIHosts.For(_renderer);

    internal IconLoader Icons => _icons ??= new IconLoader();

    internal Config Configuration => _config;

    internal IReadOnlyList<Tile> Tiles => _tiles;

    internal bool HotCornersOn => Setting("hot_corners") ? _options.HotCorners : _config.HotCorners;

    internal double EdgeBandNow => Setting("edge_band") ? _options.EdgeBand : _config.EdgeBand;

    internal int MinWidthNow => Setting("min_width") ? _options.MinWidth : _config.MinWidth;

    internal int StartOutputNow => Setting("start_output") ? _options.StartOutput : _config.StartOutput;

    private bool Setting(string name) => _options.Explicit.Contains(name);

    private void LoadConfig()
    {
        _config = Config.Load(_options.ConfigPath, _log);
        Fonts.SetConfigured(_config.Font);
        BuildTiles();
    }

    internal void Reload()
    {
        var previous = _config;
        _config = Config.Load(_options.ConfigPath, _log);
        if (!ReferenceEquals(previous?.Font, _config.Font))
        {
            Fonts.SetConfigured(_config.Font);
        }

        BuildTiles();
        foreach (var view in Views)
        {
            view.Host.MaxCells = _config.MaxCells;
            view.Start?.SetTiles(_tiles, _config.GroupOrder);
            if (view.Start is { } start)
            {
                start.Background = _config.Background;
            }
        }

        foreach (var app in _apps)
        {
            ApplyRules(app);
        }

        RelayoutAll();
        BasinReport.Line($"RELOAD tiles={_tiles.Count} rules={_config.Rules.Count}");
    }

    internal void ApplyRules(AppWindow app)
    {
        app.MinWidth = 0;
        foreach (var rule in _config.Rules)
        {
            if (rule.AppId == app.AppId && rule.MinWidth > 0)
            {
                app.MinWidth = rule.MinWidth;
            }
        }
    }

    private void BuildTiles()
    {
        _tiles.Clear();
        _entries.Clear();
        if (_config.ScanDesktopFiles)
        {
            _entries.AddRange(DesktopEntries.Scan());
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

                _tiles.Add(new Tile
                {
                    Name = entry.Name,
                    Exec = entry.Exec,
                    Icon = entry.Icon ?? Path.GetFileNameWithoutExtension(entry.Id),
                    Color = AccentOf(entry.Id),
                    Size = taken % 7 == 0 ? TileSize.Wide : TileSize.Square,
                    Group = taken < 12 ? "Main" : "More",
                });
                taken++;
            }
        }

        foreach (var view in Views)
        {
            view.Start?.SetTiles(_tiles, _config.GroupOrder);
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
        view.Start = new StartScreen(UIHost, view.BackgroundFrame, Icons, _config.Background);
        view.Start.SetTiles(_tiles, _config.GroupOrder);
        view.Splash = new ChromeSurface(UIHost, view.SplashFrame) { Enabled = false };
    }

    private void InvalidateStart()
    {
        foreach (var view in Views)
        {
            view.Start?.Invalidate();
        }
    }

    private void PaintStart(ShellView view)
    {
        if (view.Start is not { } start || !view.Background.Enabled)
        {
            return;
        }

        start.Resize(view.Box.Width, view.Box.Height, view.Scale);
        if (start.Dirty)
        {
            start.Draw();
        }

        start.Enabled = true;
    }

    internal bool StartTapped(ShellView view, double localX, double localY, bool pressed)
    {
        if (view.Start is not { } start || !view.Background.Enabled)
        {
            return false;
        }

        var tile = start.TileAt(localX, localY);
        if (pressed)
        {
            start.Pressed = tile;
            start.SetContact(localX, localY);
            if (tile is not null && AnimationsOn)
            {
                tile.Press.Start(AnimationCatalog.Of(Animation.PointerDown), _clockMillis);
            }

            return tile is not null;
        }

        var released = start.Pressed;
        start.Pressed = null;
        if (released is null || !ReferenceEquals(released, tile))
        {
            return false;
        }

        if (AnimationsOn)
        {
            released.Press.Start(AnimationCatalog.Of(Animation.PointerUp), _clockMillis);
        }

        LaunchTile(view, released);
        return true;
    }

    private ShellView? _startPan;
    private int _startPanTouch = -1;
    private bool _startPanned;

    internal bool StartPress(ShellView view, double localX, double localY, int touchId)
    {
        if (view.Start is not { } start || !view.Background.Enabled || _startPan is not null)
        {
            return false;
        }

        _startPan = view;
        _startPanTouch = touchId;
        _startPanned = false;
        if (start.AppsVisible)
        {
            start.AppsPan.Begin(localX, localY, _clockMillis);
        }
        else
        {
            start.Pan.Begin(localX, localY, _clockMillis);
        }

        if (StartTapped(view, localX, localY, pressed: true) && start.Pressed is { } tile)
        {
            start.Slide.Begin(tile, localY);
        }

        return true;
    }

    internal void TrackStartContact(double localX, double localY) =>
        _startPan?.Start?.SetContact(localX, localY);

    internal bool StartMove(double localX, double localY, int touchId)
    {
        if (_startPan is not { Start: { } start } view || touchId != _startPanTouch)
        {
            return false;
        }

        if (start.Slide.IsActive)
        {
            var stage = start.Slide.Update(localY);
            if (stage == CrossSlideStage.Detached && start.Slide.Tile is { } dragged)
            {
                dragged.DragX = 0;
                dragged.DragY = start.Slide.Travel;
                _startPanned = true;
                start.Pressed = null;
                return true;
            }

            if (Math.Abs(start.Slide.Travel) >= start.Slide.SelectThreshold)
            {
                _startPanned = true;
                start.Pressed = null;
                return true;
            }
        }

        if (start.AppsVisible)
        {
            start.AppsPan.Pan(localX, localY, _clockMillis);
            if (start.AppsPan.Axis != PanAxis.Undecided)
            {
                _startPanned = true;
                start.Pressed = null;
                start.Slide.Abort();
            }

            return true;
        }

        start.Pan.Pan(localX, localY, _clockMillis);
        if (start.Pan.Axis == PanAxis.Horizontal)
        {
            _startPanned = true;
            start.Pressed = null;
            start.Slide.Abort();
        }

        _ = view;
        return true;
    }

    internal bool StartRelease(double localX, double localY, int touchId)
    {
        if (_startPan is not { Start: { } start } view || touchId != _startPanTouch)
        {
            return false;
        }

        _startPan = null;
        _startPanTouch = -1;
        var panned = _startPanned;

        var slid = start.Slide.Tile;
        var stage = start.Slide.Release();
        if (stage is CrossSlideStage.Selected or CrossSlideStage.Detached && slid is not null)
        {
            FinishCrossSlide(view, start, slid, stage);
            return true;
        }

        if (start.AppsVisible)
        {
            start.AppsPan.Release(_clockMillis);
            if (start.AppsPan.Axis == PanAxis.Vertical && start.AppsPan.Velocity > 200)
            {
                ShowApps(view, false);
                return true;
            }
        }
        else
        {
            var vertical = start.Pan.Axis == PanAxis.Vertical;
            var speed = start.Pan.Velocity;
            start.Pan.Release(_clockMillis, GroupSnapPoints(start));
            if (vertical && speed > 200 && _config.AppsView)
            {
                ShowApps(view, true);
                return true;
            }
        }

        if (!panned)
        {
            if (start.ZoomedOut && start.GroupAt(localX, localY) is { } group)
            {
                ZoomToGroup(view, start, group);
                return true;
            }

            StartTapped(view, localX, localY, pressed: false);
        }
        else
        {
            start.Pressed = null;
        }

        return true;
    }

    private void FinishCrossSlide(ShellView view, StartScreen start, Tile tile, CrossSlideStage stage)
    {
        tile.DragX = 0;
        tile.DragY = 0;
        if (stage == CrossSlideStage.Selected)
        {
            tile.Selected = !tile.Selected;
            if (AnimationsOn)
            {
                tile.Check.Start(
                    AnimationCatalog.Of(tile.Selected ? Animation.SwipeSelect : Animation.SwipeDeselect),
                    _clockMillis);
            }

            start.Invalidate();
            BasinReport.Line($"SELECT {tile.Name} {(tile.Selected ? "on" : "off")}");
            return;
        }

        Reorder(start, tile);
        _ = view;
    }

    private void Reorder(StartScreen start, Tile tile)
    {
        foreach (var group in start.Grid.Groups)
        {
            var index = group.Tiles.IndexOf(tile);
            if (index < 0)
            {
                continue;
            }

            group.Tiles.RemoveAt(index);
            var target = Math.Clamp(index + (start.Slide.Travel > 0 ? 1 : -1), 0, group.Tiles.Count);
            group.Tiles.Insert(target, tile);
            start.SetTiles(AllTilesOf(start), _config.GroupOrder);
            BasinReport.Line($"REORDER {tile.Name} {index}->{target}");
            return;
        }
    }

    private static List<Tile> AllTilesOf(StartScreen start)
    {
        var all = new List<Tile>();
        foreach (var group in start.Grid.Groups)
        {
            all.AddRange(group.Tiles);
        }

        return all;
    }

    internal void ShowApps(ShellView view, bool visible)
    {
        if (view.Start is not { } start || start.AppsVisible == visible)
        {
            return;
        }

        if (visible)
        {
            start.SetApps(_entries);
        }

        start.AppsVisible = visible;
        start.AppsPan.Reset(0);
        Animate(ref view.StartMotion, view.BackgroundFrame, Animation.EnterPage, offsetScale: view.Scale);
        BasinReport.Line($"APPS {(visible ? "on" : "off")}");
    }

    internal void ToggleZoom(ShellView view, bool zoomOut, double centerX = -1, double centerY = -1)
    {
        if (view.Start is not { } start || start.ZoomedOut == zoomOut)
        {
            return;
        }

        var x = centerX < 0 ? view.Box.Width / 2.0 : centerX;
        var y = centerY < 0 ? view.Box.Height / 2.0 : centerY;
        start.SetZoom(
            zoomOut,
            x,
            y,
            AnimationCatalog.Of(AnimationsOn ? Animation.CrossFadeIn : Animation.FadeIn),
            _clockMillis);
        if (!AnimationsOn)
        {
            start.SetZoomNow(zoomOut);
        }

        BasinReport.Line($"ZOOM {(zoomOut ? "out" : "in")}");
    }

    private void ZoomToGroup(ShellView view, StartScreen start, TileGroup group)
    {
        ToggleZoom(view, zoomOut: false);
        start.Pan.Reset(-group.Box.X);
        BasinReport.Line($"ZOOM group={group.Name}");
    }

    private readonly double[] _snapScratch = new double[64];

    private ReadOnlySpan<double> GroupSnapPoints(StartScreen start)
    {
        var count = Math.Min(start.Grid.Groups.Count, _snapScratch.Length);
        for (var i = 0; i < count; i++)
        {
            _snapScratch[i] = -start.Grid.Groups[i].Box.X;
        }

        return _snapScratch.AsSpan(0, count);
    }

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
                if (tile.Badge != line)
                {
                    tile.Badge = line;
                    InvalidateStart();
                    if (AnimationsOn)
                    {
                        tile.Check.Start(AnimationCatalog.Of(Animation.UpdateBadge), _clockMillis);
                    }
                }
            }
            else if (tile.Peek != line)
            {
                tile.Peek = line;
                InvalidateStart();
                if (AnimationsOn)
                {
                    tile.Press.Start(AnimationCatalog.Of(Animation.Peek), _clockMillis);
                }
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

    internal void LaunchTile(ShellView view, Tile tile)
    {
        ShowSplash(view, tile.Name, tile.Color);
        Spawn(tile.Exec);
        BasinReport.Line($"LAUNCH {tile.Name}");
    }

    internal const long SplashTimeoutMillis = 10_000;

    internal void ShowSplash(ShellView view, string title, uint color)
    {
        if (view.Splash is not { } splash)
        {
            return;
        }

        view.SplashTitle = title;
        view.SplashColor = color;
        view.SplashDeadlineMillis = Environment.TickCount64 + SplashTimeoutMillis;
        splash.Enabled = true;
        view.SplashFrame.Enabled = true;
        Tween.Reset(view.SplashFrame);
        PaintSplash(view);
    }

    private void PaintSplash(ShellView view)
    {
        if (view.Splash is not { } splash || !splash.Enabled ||
            !splash.Place(new Box(0, 0, view.Box.Width, view.Box.Height), view.Scale))
        {
            return;
        }

        if (splash.BeginDraw() is not { } canvas)
        {
            return;
        }

        try
        {
            canvas.Clear(new SKColor(view.SplashColor));
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            using var font = new SKFont(Fonts.Semibold, 42) { Subpixel = true };
            canvas.DrawText(
                view.SplashTitle,
                view.Box.Width / 2f,
                view.Box.Height / 2f,
                SKTextAlign.Center,
                font,
                paint);
        }
        finally
        {
            splash.EndDraw();
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
                BasinReport.Line($"SPLASH timeout {view.SplashTitle}");
                DismissSplash(view, crossFade: false);
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
        if (!view.SplashMotion.IsRunning)
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
        Fonts.Release();
        _uiHost?.Dispose();
        _uiHost = null;
    }
}
