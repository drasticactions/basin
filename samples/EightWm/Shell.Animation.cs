using Basin.Scene;

namespace EightWm;

internal sealed partial class Shell
{
    private long _clockMillis;

    internal bool AnimationsOn =>
        _liveAnimations ?? (_options.Explicit.Contains("animations") ? _options.Animations : _config.Animations);

    internal long ClockMillis => _clockMillis;

    internal static double PanelTravel(double logicalWidth)
    {
        var full = AnimationCatalog.Of(Animation.ShowPanel).Offset.From;
        return full <= 0 ? 1 : logicalWidth / full;
    }

    internal void Animate(AppWindow app, Animation name, int index = 0, double offsetScale = 1) =>
        Animate(app, AnimationCatalog.Of(name), index, offsetScale);

    internal void Animate(AppWindow app, in AnimationSpec spec, int index = 0, double offsetScale = 1)
    {
        if (!AnimationsOn)
        {
            app.Motion.Stop();
            Tween.Reset(app.Frame);
            Kick();
            return;
        }

        _clockMillis = Environment.TickCount64;
        app.Motion.OffsetScale = offsetScale;
        app.Motion.Start(spec, _clockMillis, index);
        app.Motion.Apply(app.Frame);
        Kick();
    }

    internal void Animate(ref Tween tween, SceneTransform node, Animation name, int index = 0, double offsetScale = 1) =>
        Animate(ref tween, node, AnimationCatalog.Of(name), index, offsetScale);

    internal void Animate(ref Tween tween, SceneTransform node, in AnimationSpec spec, int index = 0, double offsetScale = 1)
    {
        if (!AnimationsOn)
        {
            tween.Stop();
            Tween.Reset(node);
            Kick();
            return;
        }

        _clockMillis = Environment.TickCount64;
        tween.OffsetScale = offsetScale;
        tween.Start(spec, _clockMillis, index);
        tween.Apply(node);
        Kick();
    }

    private void Kick()
    {
        var views = Views;
        for (var i = 0; i < views.Count; i++)
        {
            views[i].Scheduler?.ScheduleRepaint();
        }
    }

    private void AdvanceAnimations(ShellView view)
    {
        _clockMillis = Environment.TickCount64;
        PaintStart(view);
        AdvanceFlip(view, _clockMillis);
        if (view.Flip is not null)
        {
            view.Scheduler?.ScheduleRepaint();
        }

        if (AnimationsOn)
        {
            foreach (var app in _apps)
            {
                if (app.Motion.IsRunning && ReferenceEquals(HomeOf(app), view))
                {
                    app.Motion.Advance(_clockMillis);
                    app.Motion.Apply(app.Frame);
                }
            }

            AdvanceShellAnimations(view, _clockMillis);
            AdvanceSplash(view, _clockMillis);
            AdvanceCharms(view, _clockMillis);
            AdvanceTitle(view, _clockMillis);
            AdvanceSwitcher(view, _clockMillis);
            if (Animating(view))
            {
                view.Scheduler?.ScheduleRepaint();
            }
        }

        if (_scalesStale)
        {
            AnnounceScales();
        }

        _seat.RefreshCursor();
    }

    private bool Animating(ShellView view)
    {
        foreach (var app in _apps)
        {
            if (app.Motion.IsRunning && ReferenceEquals(HomeOf(app), view))
            {
                return true;
            }
        }

        if (view.StartMotion.IsRunning || view.SplashMotion.IsRunning || view.SwitcherMotion.IsRunning ||
            view.StartPageMotion.IsRunning || view.AppsMotion.IsRunning)
        {
            return true;
        }

        if (view.Charms is { } charms &&
            (charms.BarMotion.IsRunning || charms.ClockMotion.IsRunning || charms.PaneMotion.IsRunning))
        {
            return true;
        }

        if (view.Title is { } title && title.Motion.IsRunning)
        {
            return true;
        }

        return false;
    }

    private void AdvanceShellAnimations(ShellView view, long nowMillis)
    {
        if (view.StartMotion.IsRunning)
        {
            view.StartMotion.Advance(nowMillis);
            view.StartMotion.Apply(view.BackgroundFrame);
        }

        if (view.StartPageMotion.IsRunning)
        {
            view.StartPageMotion.Advance(nowMillis);
            view.StartPageMotion.Apply(view.StartFrame);
        }

        if (view.AppsMotion.IsRunning)
        {
            view.AppsMotion.Advance(nowMillis);
            view.AppsMotion.Apply(view.AppsFrame);
            if (!view.AppsMotion.IsRunning && !view.AppsVisible)
            {
                view.AppsFrame.Enabled = false;
            }
        }
    }

    internal void SettleAnimations()
    {
        AvaWin.Animations.WinAnimations.TimeScale = 0;
        foreach (var app in _apps)
        {
            app.Motion.Settle();
            app.Motion.Apply(app.Frame);
        }

        foreach (var view in Views)
        {
            FinishFlip(view);
            view.StartMotion.Settle();
            view.StartMotion.Apply(view.BackgroundFrame);
            view.StartPageMotion.Settle();
            view.StartPageMotion.Apply(view.StartFrame);
            view.AppsMotion.Settle();
            view.AppsMotion.Apply(view.AppsFrame);
            view.AppsFrame.Enabled = view.AppsVisible;

            view.SwitcherMotion.Settle();
            view.SwitcherMotion.Apply(view.SwitcherFrame);
            SettleSwitcher(view);

            if (view.Charms is { } charms)
            {
                charms.BarMotion.Settle();
                charms.BarMotion.Apply(view.CharmsFrame);
                charms.ClockMotion.Settle();
                charms.ClockMotion.Apply(view.CharmsClockFrame);
                charms.PaneMotion.Settle();
                charms.PaneMotion.Apply(view.CharmsPaneFrame);
                view.DimFrame.Alpha = DimFor(charms.BarMotion);
                SettleCharms(view, charms);
            }

            if (view.Title is { } title)
            {
                title.Motion.Settle();
                title.Motion.Apply(view.TitleFrame);
                SettleTitle(view, title);
            }
        }
    }
}
