using Basin;
using Basin.Diagnostics;
using Basin.Effects;
using Basin.Freedesktop;
using Avalonia;
using Avalonia.Controls;
using Basin.Scene;

namespace EightWm;

internal sealed partial class Shell
{
    internal const uint FlipMillis = 750;

    internal const double FlipRecede = 0.9;

    internal const double FlipCamera = 2.0;

    internal const int EntrySquare = 40;

    private long _focusClock;

    internal void LaunchTile(ShellView view, Tile tile)
    {
        FinishFlip(view);
        RestorePage(view);
        CountLaunch(tile);
        if (RunningFor(tile) is { } running)
        {
            _report.Line($"LAUNCH {tile.Name} running={running.AppId}");
            if (!ReferenceEquals(HomeOf(running), view) || !BeginFlip(view, tile, running))
            {
                Show(running);
            }

            return;
        }

        if (!BeginFlip(view, tile, null))
        {
            ShowSplash(view, tile.Name, tile.Color, TargetCell(view));
        }

        Spawn(tile.Exec);
        _report.Line($"LAUNCH {tile.Name}");
    }

    private void CountLaunch(Tile tile)
    {
        var launches = _launches.GetValueOrDefault(tile.Exec) + 1;
        _launches[tile.Exec] = launches;
        foreach (var other in Views)
        {
            foreach (var app in other.StartModel.Apps)
            {
                if (app.Exec == tile.Exec)
                {
                    app.Launches = launches;
                }
            }

            other.StartModel.Resort();
        }
    }

    internal AppWindow? RunningFor(Tile tile)
    {
        AppWindow? best = null;
        foreach (var app in _apps)
        {
            if (app.IsTransient || app.Closing || !Matches(tile, app))
            {
                continue;
            }

            if (best is null || app.FocusStamp > best.FocusStamp)
            {
                best = app;
            }
        }

        return best;
    }

    private bool Matches(Tile tile, AppWindow app)
    {
        if (app.AppId is not { Length: > 0 } appId)
        {
            return false;
        }

        var entry = _desktop.FindForAppId(appId);
        if (tile.DesktopId is { } desktopId)
        {
            return entry?.Id == desktopId;
        }

        var program = ProgramOf(tile.Exec);
        if (program.Length == 0)
        {
            return false;
        }

        return string.Equals(program, appId, StringComparison.OrdinalIgnoreCase) ||
            (entry?.Exec is { } exec && string.Equals(program, ProgramOf(exec), StringComparison.OrdinalIgnoreCase));
    }

    private static string ProgramOf(string exec)
    {
        var argv = ExecLine.Split(exec);
        return argv.Length == 0 ? string.Empty : Path.GetFileName(argv[0]);
    }

    internal Box TargetCell(ShellView view)
    {
        var host = view.Host;
        if (host.HasVacancy && !host.VacantArea.IsEmpty)
        {
            return host.VacantArea;
        }

        if (!host.IsEmpty && (host.Active ?? host.Cells[^1]) is { Cell.IsEmpty: false } outgoing)
        {
            return outgoing.Cell;
        }

        return AppArea(view);
    }

    private Box CellFor(ShellView view, AppWindow app) =>
        view.Host.Holds(app) && !app.Cell.IsEmpty ? app.Cell : TargetCell(view);

    private static (Control Source, Box Box)? SourceOf(ShellView view, Tile tile)
    {
        Control source;
        Box box;
        if (view.AppsVisible)
        {
            var index = view.StartModel.FilteredApps.IndexOf(tile);
            if (view.AppsView is not { } apps || index < 0 || apps.List.ContainerFromIndex(index) is not { } entry ||
                entry.TranslatePoint(default, apps) is not { } at)
            {
                return null;
            }

            source = entry;
            box = new Box(
                (int)Math.Round(at.X),
                (int)Math.Round(at.Y + ((entry.Bounds.Height - EntrySquare) / 2)),
                EntrySquare,
                EntrySquare);
        }
        else
        {
            var index = view.StartModel.Tiles.IndexOf(tile);
            if (view.StartView is not { } start || index < 0 || start.TileList.ContainerFromIndex(index) is not { } container ||
                container.TranslatePoint(default, start) is not { } at)
            {
                return null;
            }

            source = container;
            box = new Box(
                (int)Math.Round(at.X),
                (int)Math.Round(at.Y),
                (int)Math.Round(container.Bounds.Width),
                (int)Math.Round(container.Bounds.Height));
        }

        var visible = box.X < view.Box.Width && box.Right > 0 && box.Y < view.Box.Height && box.Bottom > 0;
        return visible && !box.IsEmpty ? (source, box) : null;
    }

    private bool BeginFlip(ShellView view, Tile tile, AppWindow? app)
    {
        if (!AnimationsOn || !view.StartVisible || view.FlipFace is not { } face ||
            SourceOf(view, tile) is not var (source, from))
        {
            return false;
        }

        var to = app is null ? TargetCell(view) : CellFor(view, app);
        if (to.IsEmpty)
        {
            return false;
        }

        var page = view.AppsVisible ? view.AppsFrame : view.StartFrame;
        var flip = new LaunchFlip(tile, source, from, to, page, app);
        source.Opacity = 0;
        if (view.AppsVisible)
        {
            view.FlipModel.ShowEntry(tile);
        }
        else
        {
            view.FlipModel.ShowTile(tile);
        }

        var magnify = Math.Clamp(Math.Min((double)to.Width / from.Width, (double)to.Height / from.Height) / 2, 1, 4);
        face.Place(from, view.Scale * magnify);
        face.Enabled = true;
        view.FlipFrame.Enabled = true;
        if (app is null && PrepareSplash(view, tile.Name, tile.Color, to))
        {
            view.SplashFrame.Alpha = 0f;
        }

        SetStartInput(view, enabled: false);
        _clockMillis = Environment.TickCount64;
        flip.StartMillis = _clockMillis;
        view.Flip = flip;
        PoseFlip(view, flip, 0);
        Kick();
        _report.Line($"FLIP begin {tile.Name} from={Describe(from)} to={Describe(to)}");
        return true;
    }

    private static string Describe(in Box box) => $"{box.X},{box.Y},{box.Width}x{box.Height}";

    private void SetStartInput(ShellView view, bool enabled)
    {
        if (view.Start is { } start)
        {
            start.InputEnabled = enabled;
        }

        if (view.AppsSurface is { } apps)
        {
            apps.InputEnabled = enabled;
        }
    }

    private void AdvanceFlip(ShellView view, long nowMillis)
    {
        if (view.Flip is not { } flip)
        {
            return;
        }

        var progress = (nowMillis - flip.StartMillis) / (double)FlipMillis;
        if (progress >= 1)
        {
            FinishFlip(view);
            return;
        }

        PoseFlip(view, flip, Math.Max(0, progress));
    }

    private void PoseFlip(ShellView view, LaunchFlip flip, double progress)
    {
        var eased = Curves.Evaluate(AnimationCurve.EaseInOut, progress);
        var from = flip.From;
        var to = flip.To;
        var x = Lerp(from.X, to.X, eased);
        var y = Lerp(from.Y, to.Y, eased);
        var width = Lerp(from.Width, to.Width, eased);
        var height = Lerp(from.Height, to.Height, eased);
        var yaw = Math.PI * eased;
        var camera = FlipCamera * Math.Max(to.Width, to.Height);

        if (yaw < Math.PI / 2)
        {
            var edgeOn = Math.Cos(yaw) < 0.01;
            view.FlipFrame.Alpha = edgeOn ? 0f : 1f;
            if (!edgeOn)
            {
                view.FlipFrame.Matrix = Pose(from, x, y, width, height, yaw, camera);
            }
        }
        else
        {
            if (!flip.BackShown)
            {
                ShowBack(view, flip);
            }

            view.FlipFrame.Alpha = 0f;
            var back = yaw - Math.PI;
            var edgeOn = Math.Cos(back) < 0.01;
            if (flip.BackIsApp && flip.App is { } app)
            {
                app.Slot.SetPosition(to.X, to.Y);
                app.Slot.Enabled = true;
                app.Frame.Alpha = edgeOn ? 0f : 1f;
                if (!edgeOn)
                {
                    var content = new Box(0, 0, Math.Max(1, app.Cell.Width), Math.Max(1, app.Cell.Height));
                    app.Frame.Matrix = Pose(content, x - to.X, y - to.Y, width, height, back, camera);
                }
            }
            else
            {
                view.SplashFrame.Alpha = edgeOn ? 0f : 1f;
                if (!edgeOn)
                {
                    view.SplashFrame.Matrix = Pose(to, x, y, width, height, back, camera);
                }
            }
        }

        Recede(view, flip.Page, eased);
    }

    private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);

    private static RenderTransform Pose(
        in Box content, double x, double y, double width, double height, double yaw, double camera)
    {
        var centerX = x + (width / 2);
        var centerY = y + (height / 2);
        var sin = Math.Sin(yaw);
        var cos = Math.Cos(yaw);
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return Projection.MapRect(
            content,
            Corner(-halfWidth, -halfHeight, centerX, centerY, sin, cos, camera),
            Corner(halfWidth, -halfHeight, centerX, centerY, sin, cos, camera),
            Corner(-halfWidth, halfHeight, centerX, centerY, sin, cos, camera),
            Corner(halfWidth, halfHeight, centerX, centerY, sin, cos, camera));
    }

    private static (double X, double Y) Corner(
        double u, double v, double centerX, double centerY, double sin, double cos, double camera)
    {
        var depth = Math.Max(1e-6, camera - (u * sin));
        var scale = camera / depth;
        return (centerX + (u * cos * scale), centerY + (v * scale));
    }

    private static void Recede(ShellView view, SceneTransform page, double progress)
    {
        var scale = 1 - ((1 - FlipRecede) * progress);
        var centerX = view.Box.Width / 2.0;
        var centerY = view.Box.Height / 2.0;
        page.Matrix = RenderTransform.Multiply(
            RenderTransform.Translation(centerX, centerY),
            RenderTransform.Multiply(RenderTransform.Scale(scale, scale), RenderTransform.Translation(-centerX, -centerY)));
        page.Alpha = (float)(1 - progress);
    }

    private void ShowBack(ShellView view, LaunchFlip flip)
    {
        flip.BackShown = true;
        if (flip.App is { Closing: false } app && _apps.Contains(app))
        {
            flip.BackIsApp = true;
            if (!flip.Revealing && view.Splash is { } splash)
            {
                splash.Enabled = false;
                Tween.Reset(view.SplashFrame);
            }
        }

        _report.Line($"FLIP back {flip.Tile.Name} {(flip.BackIsApp ? "app" : "splash")}");
    }

    internal void FinishFlip(ShellView view)
    {
        if (view.Flip is not { } flip)
        {
            return;
        }

        if (!flip.BackShown)
        {
            ShowBack(view, flip);
        }

        view.Flip = null;
        view.FlipFrame.Enabled = false;
        Tween.Reset(view.FlipFrame);
        if (view.FlipFace is { } face)
        {
            face.Enabled = false;
        }

        SetStartInput(view, enabled: true);
        flip.Source.Opacity = 1;
        Tween.Reset(view.SplashFrame);
        if (flip.App is { } app && _apps.Contains(app))
        {
            Tween.Reset(app.Frame);
            Tween.Reset(flip.Page);
            Show(app);
            if (!flip.BackIsApp)
            {
                DismissSplash(view, crossFade: true);
            }
        }
        else if (view.Splash is { Enabled: true })
        {
            Recede(view, flip.Page, 1);
            view.RecededPage = flip.Page;
        }
        else
        {
            Tween.Reset(flip.Page);
        }

        Kick();
        _report.Line($"FLIP end {flip.Tile.Name}");
    }

    internal static void RestorePage(ShellView view)
    {
        if (view.RecededPage is { } page)
        {
            view.RecededPage = null;
            Tween.Reset(page);
        }
    }
}
