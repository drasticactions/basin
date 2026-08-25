using Basin;
using Basin.Seat;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private void AttachSwitcher(ShellView view) =>
        view.Switcher = new SwitcherRail(view.SwitcherFrame) { Enabled = false };

    internal void DockSwitcher(ShellView view, bool docked)
    {
        if (view.Switcher is not { } rail)
        {
            return;
        }

        if (docked)
        {
            CloseOtherChrome(view, ChromePanel.Switcher);
        }

        if (view.SwitcherDocked == docked)
        {
            return;
        }

        view.SwitcherDocked = docked;
        if (docked)
        {
            rail.Rebuild(view.Host.Mru, view.Box.Height, view.Scale);
            rail.Enabled = true;
        }

        Animate(
            ref view.SwitcherMotion, view.SwitcherFrame,
            docked ? Animation.ShowPanel : Animation.HidePanel,
            offsetScale: -PanelTravel(SwitcherRail.RailWidth));
        BasinReport.Line($"SWITCHER {(docked ? "on" : "off")} entries={rail.Count}");
        if (!view.SwitcherMotion.IsRunning)
        {
            SettleSwitcher(view);
        }
    }

    private static void SettleSwitcher(ShellView view)
    {
        if (view.SwitcherDocked || view.Switcher is not { } rail)
        {
            return;
        }

        rail.Enabled = false;
        Tween.Reset(view.SwitcherFrame);
    }

    internal void RefreshSwitcher(ShellView view)
    {
        if (view is { SwitcherDocked: true, Switcher: { } rail })
        {
            rail.Rebuild(view.Host.Mru, view.Box.Height, view.Scale);
        }
    }

    private void AdvanceSwitcher(ShellView view, long nowMillis)
    {
        if (!view.SwitcherMotion.IsRunning)
        {
            return;
        }

        view.SwitcherMotion.Advance(nowMillis);
        view.SwitcherMotion.Apply(view.SwitcherFrame);
        if (!view.SwitcherMotion.IsRunning)
        {
            SettleSwitcher(view);
        }
    }

    internal void SwitchToPrevious(ShellView view)
    {
        if (view.Host.Previous() is not { } previous)
        {
            if (view.Host.Mru.Count > 0 && view.StartVisible)
            {
                Show(view.Host.Mru[0]);
            }

            return;
        }

        Show(previous);
        Animate(previous, Animation.EnterPage, offsetScale: -view.Scale);
        BasinReport.Line($"SWITCH {previous.AppId}");
    }

    internal void SnapPrevious(ShellView view, double fraction)
    {
        if (view.Host.Previous() is not { } previous)
        {
            return;
        }

        var at = fraction <= 0.5 ? 0 : view.Host.SlotCount;
        if (!Snap(previous, view, at, Math.Clamp(fraction, 0.2, 0.8)))
        {
            BasinReport.Line($"ERR no room to snap");
            return;
        }

        BasinReport.Line($"SNAP {previous.AppId} {at}");
    }

    internal AppWindow? SwitcherEntryAt(ShellView view, double localX, double localY) =>
        view is { SwitcherDocked: true, Switcher: { } rail } ? rail.EntryAt(localX, localY) : null;

    private AppWindow? _railDrag;
    private ShellView? _railView;
    private int _railTouch = -1;
    private bool _railMoved;

    internal AppWindow? DraggedEntry => _railDrag;

    internal bool RailPress(ShellView view, double localX, double localY, int touchId)
    {
        if (view is not { SwitcherDocked: true, Switcher: { } rail })
        {
            return false;
        }

        var box = rail.Box;
        if (localX < box.X || localX >= box.Right)
        {
            DockSwitcher(view, false);
            return true;
        }

        if (rail.EntryAt(localX, localY) is not { } app)
        {
            return true;
        }

        _railDrag = app;
        _railView = view;
        _railTouch = touchId;
        _railMoved = false;
        return true;
    }

    internal bool RailMove(ShellView view, double localX, double localY, int touchId)
    {
        if (_railDrag is not { } app || touchId != _railTouch || _railView is not { } from)
        {
            return false;
        }

        if (!_railMoved && from.Switcher is { } rail && localX > rail.Box.Right)
        {
            _railMoved = true;
            LiftFromRail(from, app);
        }

        if (!_railMoved)
        {
            return true;
        }

        CarryTo(from, app, localX, localY);
        ShowPreview(from, LandingBox(from, app, RailDropAt(view, localX)));
        return true;
    }

    private void LiftFromRail(ShellView view, AppWindow app)
    {
        DockSwitcher(view, false);
        if (app.Slot.IsDestroyed)
        {
            return;
        }

        app.Slot.Reparent(view.Dragging);
        app.Slot.ClipBox = default;
        app.Slot.Enabled = true;
        if (AnimationsOn)
        {
            Animate(app, PickUp);
        }
        else
        {
            app.Frame.Matrix = RenderTransform.Scale(TitleDragScale, TitleDragScale);
        }
    }

    private void CarryTo(ShellView view, AppWindow app, double localX, double localY)
    {
        if (app.Slot.IsDestroyed)
        {
            return;
        }

        var cell = app.Cell.IsEmpty ? AppArea(view) : app.Cell;
        var factor = DragScaleOf(app);
        app.Slot.SetPosition(
            (int)Math.Round(localX - (cell.Width * factor / 2)),
            (int)Math.Round(localY - (cell.Height * factor / 2)));
    }

    private static int RailDropAt(ShellView view, double localX) =>
        localX < view.Box.Width / 2.0 ? 0 : view.Host.SlotCount;

    internal bool RailRelease(ShellView view, double localX, double localY, int touchId)
    {
        if (_railDrag is not { } app || touchId != _railTouch)
        {
            return false;
        }

        _railDrag = null;
        _railTouch = -1;
        var moved = _railMoved;
        _railMoved = false;
        var from = _railView;
        _railView = null;
        if (from is not null)
        {
            HidePreview(from);
        }

        if (from?.Switcher is not { } rail)
        {
            return true;
        }

        var picked = DragScaleOf(app);
        if (moved && !app.Slot.IsDestroyed)
        {
            app.Slot.Reparent(from.Apps);
        }

        if (localY >= from.Box.Height - SwitcherRail.EntryHeight)
        {
            Tween.Reset(app.Frame);
            CloseApp(app);
            RefreshSwitcher(from);
            BasinReport.Line($"RAIL close {app.AppId}");
            return true;
        }

        if (!moved || localX <= rail.Box.Right)
        {
            Show(app);
            DockSwitcher(from, false);
            BasinReport.Line($"RAIL show {app.AppId}");
        }
        else
        {
            var at = RailDropAt(view, localX);
            if (!Snap(app, view, at))
            {
                Show(app);
            }

            RefreshSwitcher(from);
            DockSwitcher(from, false);
            BasinReport.Line($"RAIL snap {app.AppId} {at}");
        }

        if (!moved)
        {
            return true;
        }

        if (AnimationsOn)
        {
            Animate(app, PutDown(picked));
        }
        else
        {
            Tween.Reset(app.Frame);
        }

        return true;
    }

    internal void RailCancel()
    {
        if (_railView is { } view)
        {
            HidePreview(view);
            if (_railMoved && _railDrag is { Slot.IsDestroyed: false } app)
            {
                app.Slot.Reparent(view.Apps);
                Tween.Reset(app.Frame);
                if (!view.Host.Holds(app))
                {
                    app.Hidden();
                }

                Relayout(view);
            }
        }

        _railDrag = null;
        _railView = null;
        _railTouch = -1;
        _railMoved = false;
    }

    internal void FinishEdgeGesture(ShellView view, EdgeSwipeRecognizer edges)
    {
        BasinReport.Line($"EDGE {edges.Edge} {edges.Outcome} zone={edges.Zone} progress={edges.Progress:F2}");
        switch (edges.Edge)
        {
            case ScreenEdge.Left:
                switch (edges.Outcome)
                {
                    case EdgeSwipeOutcome.In:
                        DockSwitcher(view, false);
                        SwitchToPrevious(view);
                        break;

                    case EdgeSwipeOutcome.InAndBack:
                        DockSwitcher(view, true);
                        break;

                    case EdgeSwipeOutcome.Hold:
                        DockSwitcher(view, false);
                        SnapPrevious(view, edges.Progress);
                        break;
                }

                break;

            case ScreenEdge.Right:
                if (edges.Outcome is EdgeSwipeOutcome.In or EdgeSwipeOutcome.Hold)
                {
                    ShowCharms(view, true);
                }

                break;

            case ScreenEdge.Top:
                switch (edges.Zone)
                {
                    case EdgeSwipeZone.Bottom:
                        CloseFocused();
                        break;

                    case EdgeSwipeZone.Left:
                        if (Focused is { } toLeft)
                        {
                            SnapWithin(view, toLeft, 0);
                        }

                        break;

                    case EdgeSwipeZone.Right:
                        if (Focused is { } toRight)
                        {
                            SnapWithin(view, toRight, view.Host.SlotCount);
                        }

                        break;

                    default:
                        ShowTitle(view, true);
                        break;
                }

                break;
        }
    }

    private readonly EdgeSwipeRecognizer _synthetic = new();

    internal void RunSyntheticEdge(ShellView view, ScreenEdge edge, double progress, bool hold)
    {
        _synthetic.BandWidth = EdgeBandNow;
        _synthetic.Edges = edge switch
        {
            ScreenEdge.Left => ScreenEdges.Left,
            ScreenEdge.Right => ScreenEdges.Right,
            ScreenEdge.Top => ScreenEdges.Top,
            _ => ScreenEdges.Bottom,
        };

        var width = view.Box.Width;
        var height = view.Box.Height;
        var reach = _synthetic.RevealDistance * Math.Max(progress, 0.05);
        var (startX, startY) = edge switch
        {
            ScreenEdge.Left => (2.0, height / 2.0),
            ScreenEdge.Right => (width - 2.0, height / 2.0),
            ScreenEdge.Top => (width / 2.0, 2.0),
            _ => (width / 2.0, height - 2.0),
        };

        uint time = 0;
        if (_synthetic.Begin(1, startX, startY, width, height, time) != EdgeSwipeAction.Withhold)
        {
            BasinReport.Line($"ERR the edge band refused the gesture");
            return;
        }

        for (var step = 1; step <= 8; step++)
        {
            time += 16;
            var travelled = reach * step / 8;
            var (x, y) = edge switch
            {
                ScreenEdge.Left => (startX + travelled, startY),
                ScreenEdge.Right => (startX - travelled, startY),
                ScreenEdge.Top => (startX, startY + travelled),
                _ => (startX, startY - travelled),
            };
            _synthetic.Update(1, x, y, time);
        }

        if (hold)
        {
            time += _synthetic.HoldMillis + 40;
            var (holdX, holdY) = edge switch
            {
                ScreenEdge.Left => (startX + reach, startY),
                ScreenEdge.Right => (startX - reach, startY),
                ScreenEdge.Top => (startX, startY + reach),
                _ => (startX, startY - reach),
            };
            _synthetic.Update(1, holdX, holdY, time);
        }

        time += 200;
        if (_synthetic.End(1, time) == EdgeSwipeAction.Finish)
        {
            FinishEdgeGesture(view, _synthetic);
        }
        else
        {
            BasinReport.Line($"ERR the gesture was never claimed");
        }
    }

    internal void TrackEdgeGesture(ShellView view, EdgeSwipeRecognizer edges)
    {
        if (edges.Edge == ScreenEdge.Left && view.SwitcherDocked && view.Switcher is { } rail)
        {
            var width = rail.Box.Width;
            view.SwitcherFrame.Matrix = RenderTransform.Translation(
                -width + (width * Math.Min(1, edges.Progress * 2)), 0);
        }
    }
}
