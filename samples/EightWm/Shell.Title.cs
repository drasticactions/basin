using Basin;
using Basin.Seat;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    internal const double TitleDragSlop = 12;
    internal const double TitleDragScale = 0.4;
    internal const uint PickUpMillis = 320;
    internal const uint PutDownMillis = 400;

    private static AnimationSpec PickUp => new(
        Animation.DragSourceStart, MotionAxis.None, Track.None,
        new Track(1, TitleDragScale, PickUpMillis, 0, AnimationCurve.Departure), Track.None, 0, 0);

    private static AnimationSpec PutDown(double from) => new(
        Animation.DragSourceEnd, MotionAxis.None, Track.None,
        new Track(from, 1, PutDownMillis, 0, AnimationCurve.Deceleration), Track.None, 0, 0);

    private void AttachTitle(ShellView view) => view.Title = new AppTitleBar(UIHost, view.TitleFrame);

    internal void ShowTitle(ShellView view, bool visible)
    {
        if (view.Title is not { } title)
        {
            return;
        }

        if (visible && Focused is null)
        {
            return;
        }

        if (visible)
        {
            CloseOtherChrome(view, ChromePanel.Title);
        }

        if (title.Visible == visible)
        {
            return;
        }

        if (visible)
        {
            var app = Focused!;
            title.Resize(app.Cell.IsEmpty ? AppArea(view) : app.Cell, view.Scale);
            title.Title = app.Title.Length > 0 ? app.Title : app.AppId;
        }

        title.Show(visible);
        Animate(
            ref title.Motion, view.TitleFrame,
            visible ? Animation.ShowEdgeUi : Animation.HideEdgeUi,
            offsetScale: -EdgeTravel(AppTitleBar.BarHeight));
        title.Draw();
        BasinReport.Line($"TITLE {(visible ? "on" : "off")}");
        if (!title.Motion.IsRunning)
        {
            SettleTitle(view, title);
        }
    }

    internal void ToggleTitle(ShellView view) => ShowTitle(view, view.Title is not { Visible: true });

    private static void SettleTitle(ShellView view, AppTitleBar title)
    {
        if (title.Visible)
        {
            return;
        }

        title.Retire();
        Tween.Reset(view.TitleFrame);
    }

    private void AdvanceTitle(ShellView view, long nowMillis)
    {
        if (view.Title is not { } title || !title.Motion.IsRunning)
        {
            return;
        }

        title.Motion.Advance(nowMillis);
        title.Motion.Apply(view.TitleFrame);
        if (!title.Motion.IsRunning)
        {
            SettleTitle(view, title);
        }
    }

    internal void HoverTitle(ShellView view, double localX, double localY)
    {
        if (view.Title is not { } title || _titleDrag is not null)
        {
            return;
        }

        if (!title.Visible)
        {
            if (ChromeOpen(view, ChromePanel.Title))
            {
                return;
            }

            if (Focused is { } app && !app.Cell.IsEmpty &&
                localX >= app.Cell.X && localX < app.Cell.Right &&
                localY >= app.Cell.Y && localY <= app.Cell.Y + AppTitleBar.RevealBand)
            {
                ShowTitle(view, true);
            }

            return;
        }

        if (title.HasLeft(localY))
        {
            ShowTitle(view, false);
            return;
        }

        HotTitle(view, localX, localY);
    }

    internal void HotTitle(ShellView view, double localX, double localY)
    {
        if (view.Title is not { Visible: true } title || _titleDrag is not null)
        {
            return;
        }

        var hot = title.HoldsClose(localX, localY);
        if (hot != title.HotClose)
        {
            title.HotClose = hot;
            title.Draw();
        }
    }

    private AppWindow? _titleDrag;
    private ShellView? _titleView;
    private int _titleTouch = -1;
    private double _titleGrabX;
    private double _titleGrabY;
    private bool _titleMoved;

    internal bool TitlePress(ShellView view, double localX, double localY, int touchId)
    {
        if (view.Title is not { Visible: true } title || !title.Holds(localX, localY))
        {
            return false;
        }

        if (title.HoldsClose(localX, localY))
        {
            ShowTitle(view, false);
            CloseFocused();
            return true;
        }

        if (Focused is not { } app)
        {
            return true;
        }

        _titleDrag = app;
        _titleView = view;
        _titleTouch = touchId;
        _titleGrabX = localX;
        _titleGrabY = localY;
        _titleMoved = false;
        return true;
    }

    internal bool TitleMove(ShellView view, double localX, double localY, int touchId)
    {
        if (_titleDrag is not { } app || touchId != _titleTouch || _titleView is not { } from)
        {
            return false;
        }

        if (!_titleMoved)
        {
            if (Math.Abs(localX - _titleGrabX) < TitleDragSlop &&
                Math.Abs(localY - _titleGrabY) < TitleDragSlop)
            {
                return true;
            }

            _titleMoved = true;
            from.Title!.Dragging = true;
            ShowTitle(from, false);
            app.Slot.ClipBox = default;
            if (!app.Slot.IsDestroyed)
            {
                app.Slot.Reparent(from.Dragging);
            }

            if (AnimationsOn)
            {
                Animate(app, PickUp);
            }
            else
            {
                app.Frame.Matrix = RenderTransform.Scale(TitleDragScale, TitleDragScale);
            }
        }

        var cell = app.Cell;
        var factor = DragScaleOf(app);
        var holdX = cell.Width <= 0 ? 0.5 : (_titleGrabX - cell.X) / cell.Width;
        app.Slot.SetPosition(
            (int)Math.Round(localX - (cell.Width * factor * holdX)),
            (int)Math.Round(localY - (AppTitleBar.BarHeight * factor)));
        ShowPreview(from, DropPreview(from, app, localX, localY));
        return true;
    }

    private Box DropPreview(ShellView view, AppWindow app, double localX, double localY) =>
        DropZone(view, localX, localY) switch
        {
            EdgeSwipeZone.Bottom => default,
            EdgeSwipeZone.Left => LandingBox(view, app, 0),
            EdgeSwipeZone.Right => LandingBox(view, app, view.Host.SlotCount),
            _ => AppArea(view),
        };

    internal AppWindow? DraggedTitle => _titleDrag;

    private double DragScaleOf(AppWindow app) =>
        app.Motion is { IsRunning: true, Name: Animation.DragSourceStart } motion && motion.Scale > 0
            ? motion.Scale
            : TitleDragScale;

    internal bool TitleRelease(ShellView view, double localX, double localY, int touchId)
    {
        if (_titleDrag is not { } app || touchId != _titleTouch)
        {
            return false;
        }

        var from = _titleView;
        var moved = _titleMoved;
        _titleDrag = null;
        _titleView = null;
        _titleTouch = -1;
        _titleMoved = false;
        if (from is null)
        {
            return true;
        }

        HidePreview(from);
        if (moved && !app.Slot.IsDestroyed)
        {
            app.Slot.Reparent(from.Apps);
        }

        if (from.Title is { } title)
        {
            title.Dragging = false;
        }

        if (!moved)
        {
            Tween.Reset(app.Frame);
            return true;
        }

        var picked = DragScaleOf(app);
        var zone = DropZone(from, localX, localY);
        BasinReport.Line($"TITLE drop {zone} {app.AppId}");
        switch (zone)
        {
            case EdgeSwipeZone.Bottom:
                Tween.Reset(app.Frame);
                CloseApp(app);
                _ = view;
                return true;

            case EdgeSwipeZone.Left:
                SnapWithin(from, app, 0);
                break;

            case EdgeSwipeZone.Right:
                SnapWithin(from, app, from.Host.SlotCount);
                break;

            default:
                Fill(from, app);
                break;
        }

        if (AnimationsOn)
        {
            Animate(app, PutDown(picked));
        }
        else
        {
            Tween.Reset(app.Frame);
        }

        _ = view;
        return true;
    }

    internal void TitleCancel()
    {
        if (_titleDrag is { } app)
        {
            Tween.Reset(app.Frame);
            if (_titleView is { } from)
            {
                HidePreview(from);
                if (!app.Slot.IsDestroyed)
                {
                    app.Slot.Reparent(from.Apps);
                }

                Relayout(from);
                if (from.Title is { } title)
                {
                    title.Dragging = false;
                }
            }
        }

        _titleDrag = null;
        _titleView = null;
        _titleTouch = -1;
        _titleMoved = false;
    }

    internal void SnapWithin(ShellView view, AppWindow app, int at)
    {
        if (Snap(app, view, at))
        {
            return;
        }

        view.Host.Replace(app);
        Relayout(view);
        Focus(app);
    }

    internal void Fill(ShellView view, AppWindow app)
    {
        foreach (var other in view.Host.Cells.ToArray())
        {
            if (!ReferenceEquals(other, app))
            {
                view.Host.Eject(other);
            }
        }

        view.Host.ClearVacancy();
        if (!view.Host.Holds(app))
        {
            view.Host.Replace(app);
        }

        Relayout(view);
        Focus(app);
    }

    private static EdgeSwipeZone DropZone(ShellView view, double localX, double localY)
    {
        if (localY >= view.Box.Height * 0.75)
        {
            return EdgeSwipeZone.Bottom;
        }

        if (localX <= view.Box.Width * 0.25)
        {
            return EdgeSwipeZone.Left;
        }

        return localX >= view.Box.Width * 0.75 ? EdgeSwipeZone.Right : EdgeSwipeZone.Middle;
    }

    private static double EdgeTravel(double logicalHeight)
    {
        var full = AnimationCatalog.Of(Animation.ShowEdgeUi).Offset.From;
        return full <= 0 ? 1 : logicalHeight / full;
    }
}
