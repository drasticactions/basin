using System.Globalization;
using Basin;
using Basin.Effects;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const string OverviewChangedEvent = "tinycomp/overview-changed";

    private OutputView? _overviewSwipeView;
    private bool _overviewSwipeOpening;
    private bool _overviewEscapeHeld;
    private uint? _overviewClickButton;
    private Basin.IEventSource? _hotCornerTimer;
    private OutputView? _hotCornerView;

    private static OverviewView OverviewOf(OutputView view) => view.Policy.Overview;

    private bool OverviewInUse(OutputView view) =>
        view.Tag is OutputPolicy && OverviewOf(view).Active && !CanvasWanted(view);

    private bool OverviewOpenOn(OutputView view) => view.Tag is OutputPolicy && OverviewOf(view).Open;

    private bool AnyOverviewActive()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            if (Views[i].Tag is OutputPolicy && OverviewOf(Views[i]).Active)
            {
                return true;
            }
        }

        return false;
    }

    private OutputView? OverviewTargetView() =>
        FocusedGrabTarget() is { } focused && ViewOfWindow(focused) is { Tag: OutputPolicy } focusedView
            ? focusedView
            : ViewAtCursor() is { Tag: OutputPolicy } pointed ? pointed : null;

    private static int Direction(CanvasSide side) => side is CanvasSide.Left or CanvasSide.Top ? -1 : 1;

    private static (int Outer, int Edge, double Center, int Size) SideFrame(CanvasSide side, in Box box, in Box usable)
    {
        var horizontal = (side & CanvasSide.Horizontal) != 0;
        var direction = Direction(side);
        var outer = horizontal ? (direction < 0 ? box.X : box.Right) : (direction < 0 ? box.Y : box.Bottom);
        var edge = horizontal ? (direction < 0 ? usable.X : usable.Right) : (direction < 0 ? usable.Y : usable.Bottom);
        var center = horizontal ? box.X + (box.Width / 2.0) : box.Y + (box.Height / 2.0);
        return (outer, edge, center, horizontal ? box.Width : box.Height);
    }

    private void LayoutOverviewFull(OutputView view, in Box box, bool animating)
    {
        var canvas = view.Canvas;
        var overview = OverviewOf(view);
        if (!animating)
        {
            overview.Settings = _config.OverviewFor(view.Output.Name);
        }

        var settings = canvas.Settings;
        var scale = overview.Settings.ScaleValue;
        var usable = UsableBox(view, box);
        var switched = overview.Steps != overview.Settings.Steps;
        overview.Steps = overview.Settings.Steps;
        if (overview.Steps)
        {
            LayoutStepSides(view, box, usable);
            if (!canvas.Overview || switched)
            {
                if (switched)
                {
                    overview.StepSeenValid = false;
                }

                SetStepFull(view, box, usable, StepShelfScale(view, null));
            }

            if (switched)
            {
                ReshelveAll(view);
            }

            return;
        }

        var active = CanvasSide.None;
        var border = CanvasSide.None;
        foreach (var side in CanvasSides.Each)
        {
            var index = SideIndex(side);
            var (outer, edge, center, size) = SideFrame(side, box, usable);
            var wanted = settings.SideSet.HasFlag(side) && !HasNeighbour(view.Output, box, side);
            var full = wanted
                ? OverviewLayout.Full(outer, edge, center, Direction(side), scale, settings.ShelfFraction, size)
                : new OverviewSide(false, 0, 0, 0);
            if (wanted && !full.Active)
            {
                border |= side;
            }

            if (full.Active)
            {
                active |= side;
            }

            overview.FullSides[index] = full;
            overview.ShelfScreen[index] = full.Shelf;
            overview.SlopeScreen[index] = full.Slope;
            var target = Math.Min(ShelfScaleInForce(canvas, side), scale);
            var end = OverviewLayout.At(full, 1.0, outer, edge, center, Direction(side), scale, target);
            var fan = (side & CanvasSide.Horizontal) != 0 ? box.Y + (box.Height / 2.0) : box.X + (box.Width / 2.0);
            var fullWarp = FullWarpOf(overview, side);
            _ = fullWarp.LayoutTerrace(
                outer, Direction(side), end.Zone, end.Shelf, end.EdgeScale, CanvasWarp.TerraceExponent, settings.SlopeValue, fan);
            overview.OuterCanvas[index] = fullWarp.IsIdentity ? double.NaN : fullWarp.ToCanvas(fullWarp.OuterEdge);
        }

        overview.Sides = active;
        overview.BorderSides = border;
        var map = overview.Full;
        map.ViewScale = scale;
        map.ViewCenterX = box.X + (box.Width / 2.0);
        map.ViewCenterY = box.Y + (box.Height / 2.0);
        map.Separable = settings.ShapeValue == ShelfShape.Flat;
        map.CornerRadius = settings.CornerRadiusValue;
        map.CornerTaper = settings.CornerTaperValue;
        if (switched)
        {
            ReshelveAll(view);
        }
    }

    private void LayoutOverview(OutputView view, in Box box, bool animating)
    {
        var canvas = view.Canvas;
        var overview = OverviewOf(view);
        var settings = canvas.Settings;
        LayoutOverviewFull(view, box, animating);
        if (overview.Steps)
        {
            LayoutOverviewStep(view, box, animating);
            return;
        }

        if (canvas.Step is not null)
        {
            LeaveStep(view);
        }

        var scale = overview.Settings.ScaleValue;
        var usable = UsableBox(view, box);
        var centerX = box.X + (box.Width / 2.0);
        var centerY = box.Y + (box.Height / 2.0);
        var wasOverview = canvas.Overview;
        var changed = !wasOverview;
        canvas.Overview = true;
        if (canvas.Mode != CanvasWindowMode.Terrace)
        {
            canvas.Mode = CanvasWindowMode.Terrace;
            changed = true;
        }

        var zoom = 1.0 + ((scale - 1.0) * overview.Value);
        foreach (var side in CanvasSides.Each)
        {
            var index = SideIndex(side);
            var (outer, edge, center, _) = SideFrame(side, box, usable);
            var full = overview.FullSides[index];
            var shelfScale = Math.Min(ShelfScaleFor(view, side, settings, wasOverview && full.Active && !animating), scale);
            var now = OverviewLayout.At(full, overview.Value, outer, edge, center, Direction(side), scale, shelfScale);
            var fan = (side & CanvasSide.Horizontal) != 0 ? centerY : centerX;
            changed |= WarpOf(canvas, side).LayoutTerrace(
                outer, Direction(side), now.Zone, now.Shelf, now.EdgeScale, CanvasWarp.TerraceExponent, settings.SlopeValue, fan);
        }

        var map = canvas.Map;
        if (map.ViewScale != zoom || map.ViewCenterX != centerX || map.ViewCenterY != centerY)
        {
            map.ViewScale = zoom;
            map.ViewCenterX = centerX;
            map.ViewCenterY = centerY;
            changed = true;
        }

        var separable = settings.ShapeValue == ShelfShape.Flat;
        if (map.Separable != separable)
        {
            map.Separable = separable;
            changed = true;
        }

        if (map.CornerRadius != settings.CornerRadiusValue || map.CornerTaper != settings.CornerTaperValue)
        {
            map.CornerRadius = settings.CornerRadiusValue;
            map.CornerTaper = settings.CornerTaperValue;
            changed = true;
        }

        changed |= LayoutCanvasGrid(view, box, settings.GridMode != CanvasGridMode.Never);
        SyncLayerZoom(view, zoom, centerX, centerY);
        if (!animating)
        {
            FollowShelfGeometry(view);
        }

        if (!changed)
        {
            return;
        }

        canvas.Generation++;
        if (!animating)
        {
            var line = OverviewLine(view);
            if (line != overview.Reported)
            {
                overview.Reported = line;
                _report.Line(line);
            }
        }
        else
        {
            ResolveShelfPins(view);
        }

        ApplyCanvasToView(view);
        canvas.Grid?.NotifyMeshChanged();
        view.Scheduler?.ScheduleRepaint();
    }

    private static CanvasWarp FullWarpOf(OverviewView overview, CanvasSide side) => side switch
    {
        CanvasSide.Left => overview.FullLeft,
        CanvasSide.Right => overview.FullRight,
        CanvasSide.Top => overview.FullTop,
        _ => overview.FullBottom,
    };

    private void LeaveOverviewLayout(OutputView view)
    {
        var canvas = view.Canvas;
        canvas.Overview = false;
        canvas.Map.ViewScale = 1.0;
        LeaveStep(view);
        var box = _layout.BoxOf(view.Output);
        SyncLayerZoom(view, 1.0, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        ClearShelves(view);
    }

    private void FollowShelfGeometry(OutputView view)
    {
        var overview = OverviewOf(view);
        foreach (var window in overview.Shelved)
        {
            if (!_canvasStates.TryGetValue(window, out var state) || IsCanvasMoving(state))
            {
                continue;
            }

            var dx = (state.ShelfSide & CanvasSide.Horizontal) is var horizontal and not CanvasSide.None
                ? ShelfShift(overview, horizontal)
                : 0;
            var dy = (state.ShelfSide & CanvasSide.Vertical) is var vertical and not CanvasSide.None
                ? ShelfShift(overview, vertical)
                : 0;
            if (dx != 0 || dy != 0)
            {
                window.MoveTo(window.X + dx, window.Y + dy);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            overview.OuterSeen[i] = overview.OuterCanvas[i];
        }
    }

    private static int ShelfShift(OverviewView overview, CanvasSide side)
    {
        var index = SideIndex(side);
        var before = overview.OuterSeen[index];
        var now = overview.OuterCanvas[index];
        return double.IsNaN(before) || double.IsNaN(now) ? 0 : (int)Math.Round(now - before);
    }

    private void SyncLayerZoom(OutputView view, double zoom, double centerX, double centerY)
    {
        var overview = OverviewOf(view);
        var placement = zoom == 1.0
            ? RenderTransform.Identity
            : new RenderTransform(zoom, 0, centerX * (1.0 - zoom), 0, zoom, centerY * (1.0 - zoom), 0, 0, 1);
        if (zoom != 1.0)
        {
            foreach (var (layer, scene) in _layerDriver.Surfaces)
            {
                if (scene is null || scene.Tree.IsDestroyed ||
                    layer.Layer is not (LayerKind.Background or LayerKind.Bottom) ||
                    !ReferenceEquals(layer.Output?.Output ?? (Views.Count > 0 ? Views[0].Output : null), view.Output))
                {
                    continue;
                }

                var background = layer.Layer == LayerKind.Background;
                var zoomNode = background ? overview.BackgroundZoom : overview.BottomZoom;
                if (zoomNode is not { IsDestroyed: false })
                {
                    var root = background ? _layers.Background : _layers.Bottom;
                    zoomNode = new SceneTransform(root);
                    zoomNode.LowerToBottom();
                    if (background)
                    {
                        overview.BackgroundZoom = zoomNode;
                    }
                    else
                    {
                        overview.BottomZoom = zoomNode;
                    }
                }

                if (!ReferenceEquals(scene.Tree.Parent, zoomNode))
                {
                    scene.Tree.Reparent(zoomNode);
                }
            }
        }

        if (overview.BackgroundZoom is { IsDestroyed: false } backgroundZoom && backgroundZoom.Matrix != placement)
        {
            backgroundZoom.Matrix = placement;
        }

        if (overview.BottomZoom is { IsDestroyed: false } bottomZoom && bottomZoom.Matrix != placement)
        {
            bottomZoom.Matrix = placement;
        }
    }

    private string OverviewLine(OutputView view)
    {
        var overview = OverviewOf(view);
        var index = overview.Sides.HasFlag(CanvasSide.Right) ? 1
            : overview.Sides.HasFlag(CanvasSide.Left) ? 0
            : overview.Sides.HasFlag(CanvasSide.Top) ? 2
            : 3;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"OVERVIEW output={view.Output.Name} open={(overview.Open ? "true" : "false")} progress={overview.Value:F2}"
            + $" scale={overview.Settings.ScaleValue:F2} sides={CanvasSetting.NamesOf(overview.Sides)}"
            + $" shelf={overview.ShelfScreen[index]} slope={overview.SlopeScreen[index]} wall={overview.Settings.WallName}");
        if (overview.BorderSides == CanvasSide.None)
        {
            return line;
        }

        var inactive = new List<string>(4);
        foreach (var side in CanvasSides.Each)
        {
            if (overview.BorderSides.HasFlag(side))
            {
                inactive.Add($"{ZoneName(side)}(border)");
            }
        }

        return line + $" inactive={string.Join(',', inactive)}";
    }

    private string? OverviewRefusal(OutputView view)
    {
        if (!OverviewOf(view).Settings.Enabled || !_config.OverviewFor(view.Output.Name).Enabled)
        {
            return "disabled";
        }

        if (CanvasWanted(view))
        {
            return "canvas";
        }

        if (_sessionLock.IsLocked)
        {
            return "locked";
        }

        if (_effects.SwitcherActive)
        {
            return "switcher";
        }

        return _mode != DragMode.None ? "drag" : null;
    }

    internal bool? ToggleOverview(OutputView? view, bool? open, bool animate = true)
    {
        view ??= OverviewTargetView();
        if (view is null)
        {
            _report.Line("OVERVIEW refused: no output");
            return null;
        }

        var overview = OverviewOf(view);
        var wanted = open ?? !overview.Open;
        if (!SetOverview(view, wanted, animate))
        {
            return null;
        }

        return overview.Open;
    }

    private bool SetOverview(OutputView view, bool open, bool animate = true)
    {
        var overview = OverviewOf(view);
        if (open && OverviewRefusal(view) is { } reason)
        {
            _report.Line($"OVERVIEW refused: {reason}");
            return false;
        }

        if (!open && _mode != DragMode.None && overview.Open)
        {
            _report.Line("OVERVIEW refused: drag");
            return false;
        }

        if (overview.Open == open && !overview.Progress.IsRunning && overview.Value == (open ? 1.0 : 0.0))
        {
            _report.Line(OverviewLine(view));
            return true;
        }

        if (open && !overview.Open)
        {
            EnterOverview(view);
        }

        overview.Open = open;
        overview.Tracking = false;
        var nanos = animate ? CanvasAnimationNanos(view) : 0;
        overview.Progress.Begin(overview.Value, open ? 1.0 : 0.0, nanos);
        if (!overview.Progress.IsRunning)
        {
            overview.Value = open ? 1.0 : 0.0;
        }

        LayoutCanvas(view);
        var line = OverviewLine(view);
        if (line != overview.Reported)
        {
            overview.Reported = line;
            _report.Line(line);
        }

        EmitOverviewChanged(view);
        if (overview.Progress.IsRunning)
        {
            ScheduleEffectRepaint();
        }
        else
        {
            FinishOverview(view);
        }

        return true;
    }

    private void EnterOverview(OutputView view)
    {
        var overview = OverviewOf(view);
        overview.Settings = _config.OverviewFor(view.Output.Name);
        overview.ShelfTree ??= new SceneTree(_layers.Windows);
        overview.ShelfTree.Enabled = true;
        foreach (var window in overview.Shelved)
        {
            ShowShelved(window, shown: true);
        }
    }

    private void FinishOverview(OutputView view)
    {
        var overview = OverviewOf(view);
        if (overview.Value <= 0 && !overview.Open)
        {
            overview.Value = 0;
            foreach (var window in overview.Shelved)
            {
                if (_canvasStates.TryGetValue(window, out var state))
                {
                    state.Blend.Cancel();
                    state.MotionX.Cancel();
                    state.MotionY.Cancel();
                    _canvasMotions.Remove(window);
                }

                ShowShelved(window, shown: false);
            }

            foreach (var (window, state) in _canvasStates)
            {
                if (!state.Shelved && ViewOfWindow(window) == view)
                {
                    state.Anchor = null;
                    state.Blend.Cancel();
                }
            }

            LeaveOverviewLayout(view);
            overview.Reported = null;
            LayoutCanvas(view);
            ApplyCanvasToView(view);
        }

        overview.Reported = OverviewLine(view);
        _report.Line(overview.Reported);
    }

    private bool StepOverviews(in FrameTick tick)
    {
        var running = false;
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is not OutputPolicy)
            {
                continue;
            }

            var overview = OverviewOf(view);
            if (!overview.Progress.IsRunning)
            {
                continue;
            }

            running |= overview.Progress.Step(tick, out var value);
            overview.Value = value;
            LayoutCanvas(view, animating: true);
            if (_mode == DragMode.Move && _grabWindow is { } held && _canvasStates.TryGetValue(held, out var heldState) &&
                heldState.Dragging && ViewOfWindow(held) == view)
            {
                _ = DragCanvasScaled(held, _cursorX, _cursorY);
            }

            if (!overview.Progress.IsRunning)
            {
                FinishOverview(view);
            }
        }

        return running;
    }

    private void EmitOverviewChanged(OutputView view)
    {
        if (_ipc is not { } ipc || !ipc.Events.HasSubscribers(OverviewChangedEvent))
        {
            return;
        }

        var json = ipc.Events.BeginEvent(OverviewChangedEvent);
        json.WriteStartObject();
        json.WriteString("output", view.Output.Name);
        json.WriteBoolean("open", OverviewOf(view).Open);
        json.WriteEndObject();
        ipc.Events.EndEvent();
    }

    private void DeclareOverviewEvents()
    {
        if (_ipc is { } ipc && !ipc.Events.IsDeclared(OverviewChangedEvent))
        {
            ipc.Events.Declare(OverviewChangedEvent);
        }
    }

    private void CloseOverviewsNow()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is OutputPolicy && OverviewOf(view).Active)
            {
                var overview = OverviewOf(view);
                overview.Open = false;
                overview.Tracking = false;
                overview.Progress.Cancel();
                overview.Value = 0;
                LayoutCanvas(view, animating: true);
                FinishOverview(view);
                EmitOverviewChanged(view);
            }
        }
    }

    private CanvasWindowState? ShelfStateOf(IGrabTarget window) =>
        _canvasStates.TryGetValue(window, out var state) && state.Shelved ? state : null;

    private bool IsShelved(IGrabTarget window) => ShelfStateOf(window) is not null;

    private OutputView? ShelfOwnerOf(IGrabTarget window)
    {
        if (ShelfStateOf(window) is not { Owner: { } owner })
        {
            return null;
        }

        for (var i = 0; i < Views.Count; i++)
        {
            if (ReferenceEquals(Views[i], owner))
            {
                return owner;
            }
        }

        return null;
    }

    private void ShowShelved(IGrabTarget window, bool shown)
    {
        if (window.EffectTree is { IsDestroyed: false } tree)
        {
            tree.Enabled = shown;
        }

        switch (window)
        {
            case Window w:
                w.Toplevel.SetSuspended(!shown);
                _xdgToplevels.SetMinimized(w.Toplevel, !shown);
                break;
            case XWindow x:
                x.XWin.SetMinimized(!shown);
                break;
        }
    }

    private string WindowIdOf(IGrabTarget window)
    {
        var surface = window switch
        {
            Window w => w.Toplevel.Surface,
            XWindow x => x.XWin.Surface,
            _ => null,
        };
        return ToplevelIdOf(surface) is { } id ? id.ToString(CultureInfo.InvariantCulture) : "?";
    }

    private static Workspace? WorkspaceOf(IGrabTarget window) => window switch
    {
        Window w => w.Workspace,
        XWindow x => x.Workspace,
        _ => null,
    };

    private static void SetWorkspaceOf(IGrabTarget window, Workspace? workspace)
    {
        switch (window)
        {
            case Window w:
                w.Workspace = workspace;
                break;
            case XWindow x:
                x.Workspace = workspace;
                break;
        }
    }

    private bool IsOverviewSide(OutputView view, CanvasSide side)
    {
        if (side == CanvasSide.None)
        {
            return false;
        }

        if (!view.Canvas.Overview)
        {
            LayoutOverviewFull(view, _layout.BoxOf(view.Output), animating: false);
        }

        return (OverviewOf(view).Sides & side) == side;
    }

    internal void Shelve(IGrabTarget window, CanvasSide side)
    {
        if (window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            _report.Line("SHELVE refused: no window");
            return;
        }

        if (CanvasWanted(view))
        {
            _report.Line("SHELVE refused: canvas");
            return;
        }

        if (!OverviewOf(view).Settings.Enabled || !_config.OverviewFor(view.Output.Name).Enabled)
        {
            _report.Line("SHELVE refused: disabled");
            return;
        }

        if (!IsOverviewSide(view, side))
        {
            _report.Line("SHELVE refused: side");
            return;
        }

        if (_mode != DragMode.None)
        {
            _report.Line("SHELVE refused: drag");
            return;
        }

        var overview = OverviewOf(view);
        if (ShelfStateOf(window) is { } already && already.ShelfSide == side)
        {
            _report.Line("SHELVE refused: same side");
            return;
        }

        if (overview.Steps)
        {
            if ((side & CanvasSide.Horizontal) != 0 && (side & CanvasSide.Vertical) != 0)
            {
                _report.Line("SHELVE refused: side");
                return;
            }

            StepShelve(window, view, side);
            _report.Line($"SHELVE id={WindowIdOf(window)} side={SideNames(side)} output={view.Output.Name}");
            return;
        }

        var (targetX, targetY) = SlopeReshelfTarget(view, window, side);
        var state = EnterShelf(window, view, side);
        var closed = !overview.Active;
        if (closed)
        {
            state.HideWhenParked = true;
        }

        var nanos = CanvasAnimationNanos(view);
        BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
        BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
        if (closed && !state.MotionX.IsRunning && !state.MotionY.IsRunning)
        {
            state.HideWhenParked = false;
            ShowShelved(window, shown: false);
        }

        _report.Line($"SHELVE id={WindowIdOf(window)} side={SideNames(side)} output={view.Output.Name}");
    }

    private static bool ShelfAlongKept(CanvasSide from, CanvasSide to)
    {
        var axis = (to & CanvasSide.Horizontal) != 0 ? CanvasSide.Horizontal : CanvasSide.Vertical;
        return (from & axis) != 0 && (from & ~axis) == CanvasSide.None;
    }

    private (int X, int Y) SlopeReshelfTarget(OutputView view, IGrabTarget window, CanvasSide side)
    {
        var box = CanvasBoxOf(window);
        if (ShelfStateOf(window) is not { } shelved || ShelfAlongKept(shelved.ShelfSide, side))
        {
            return SlopeShelfTarget(view, window, box, side);
        }

        var home = box.Translated(shelved.PreShelfX - window.X, shelved.PreShelfY - window.Y);
        var (x, y) = SlopeShelfTarget(view, window, home, side);
        x += home.X - box.X;
        y += home.Y - box.Y;
        return (side & CanvasSide.Horizontal) != 0 ? (x, shelved.PreShelfY) : (shelved.PreShelfX, y);
    }

    private (int X, int Y) SlopeShelfTarget(OutputView view, IGrabTarget window, in Box box, CanvasSide side)
    {
        var overview = OverviewOf(view);
        var full = overview.Full;
        var floor = view.Canvas.Settings.ShelfMinScaleValue;
        var sideWarp = (side & CanvasSide.Horizontal) is var h and not CanvasSide.None ? FullWarpOf(overview, h) : null;
        var endWarp = (side & CanvasSide.Vertical) is var v and not CanvasSide.None ? FullWarpOf(overview, v) : null;
        if (sideWarp is not null && endWarp is not null)
        {
            var (x, y) = view.Canvas.Scale.TerraceCornerParkTarget(full, sideWarp, endWarp, box, floor);
            return (x + (window.X - box.X), y + (window.Y - box.Y));
        }

        if (sideWarp is not null)
        {
            return (view.Canvas.Scale.TerraceParkTarget(full, sideWarp, box, floor) + (window.X - box.X), window.Y);
        }

        return endWarp is null
            ? (window.X, window.Y)
            : (window.X, view.Canvas.Scale.TerraceParkTarget(full, endWarp, box, floor) + (window.Y - box.Y));
    }

    private static string SideNames(CanvasSide side) => CanvasSetting.NamesOf(side).Replace(',', '-');

    private CanvasWindowState EnterShelf(IGrabTarget window, OutputView view, CanvasSide side)
    {
        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var overview = OverviewOf(view);
        if (!state.Shelved)
        {
            state.PreShelfX = window.X;
            state.PreShelfY = window.Y;
            state.PreShelfWorkspace = WorkspaceOf(window);
            var workspace = WorkspaceOf(window);
            if (workspace is not null && window is Window tiled && workspace.Tiled.Remove(tiled))
            {
                DissolveSplit(workspace);
            }

            SetWorkspaceOf(window, null);
            overview.ShelfTree ??= new SceneTree(_layers.Windows);
            window.EffectTree?.Reparent(overview.ShelfTree);
            overview.Shelved.Add(window);
            var focused = ReferenceEquals(_focused, window) || ReferenceEquals(_focusedX, window);
            state.Shelved = true;
            state.Owner = view;
            if (focused)
            {
                if (view.Active is { } active)
                {
                    FocusWorkspaceWindow(active);
                }
                else
                {
                    FocusWindow(null);
                }
            }

            _workspaceModel.RaiseMembersChanged();
        }

        state.ShelfSide = side;
        state.Home = null;
        state.Anchor = null;
        state.ShelfPinned = false;
        state.HideWhenParked = false;
        return state;
    }

    internal void Unshelve(IGrabTarget window, double? x, double? y, OutputView? into = null, Workspace? workspace = null)
    {
        if (ShelfStateOf(window) is not { } state)
        {
            _report.Line("UNSHELVE refused: not shelved");
            return;
        }

        var owner = ShelfOwnerOf(window) ?? (Views.Count > 0 ? Views[0] : null);
        var view = into ?? owner;
        if (view is null || window.EffectTree is not { IsDestroyed: false } tree)
        {
            return;
        }

        var overview = owner is null ? null : OverviewOf(owner);
        overview?.Shelved.Remove(window);
        var stepped = owner is not null && Stepped(owner);
        state.Shelved = false;
        state.Owner = null;
        state.HideWhenParked = false;
        state.ShelfPinned = false;
        state.Anchor = null;
        var target = workspace ?? view.Active;
        SetWorkspaceOf(window, target);
        tree.Reparent(window is Window w ? LayerFor(w) : target?.Tree ?? _layers.Windows);
        tree.RaiseToTop();
        var hidden = !tree.Enabled;
        ShowShelved(window, shown: true);
        if (target is not null && ViewOf(target)?.Active != target)
        {
            tree.Enabled = false;
        }

        var box = CanvasBoxOf(window);
        var usable = UsableBox(view, _layout.BoxOf(view.Output));
        var offsetX = window.X - box.X;
        var offsetY = window.Y - box.Y;
        int left;
        int top;
        if (x is { } atX && y is { } atY)
        {
            left = (int)Math.Round(atX);
            top = (int)Math.Round(atY);
        }
        else
        {
            var pre = new Box(state.PreShelfX - offsetX, state.PreShelfY - offsetY, box.Width, box.Height);
            if (!pre.Intersect(usable).IsEmpty && ReferenceEquals(view, owner))
            {
                left = pre.X;
                top = pre.Y;
            }
            else
            {
                left = usable.X + ((usable.Width - box.Width) / 2);
                top = usable.Y + ((usable.Height - box.Height) / 2);
            }
        }

        left = box.Width >= usable.Width ? usable.X : Math.Clamp(left, usable.X, usable.Right - box.Width);
        top = box.Height >= usable.Height ? usable.Y : Math.Clamp(top, usable.Y, usable.Bottom - box.Height);
        var targetX = left + offsetX;
        var targetY = top + offsetY;
        state.StepView = null;
        if (overview is { Active: true } && ReferenceEquals(view, owner) && !hidden && stepped)
        {
            JumpWithBlend(window, view, state, Basin.Effects.CanvasStepPlane.Desktop, targetX, targetY);
        }
        else if (overview is { Active: true } && ReferenceEquals(view, owner) && !hidden)
        {
            state.StepPlane = Basin.Effects.CanvasStepPlane.Desktop;
            var nanos = CanvasAnimationNanos(view);
            BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
            BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
        }
        else
        {
            state.StepPlane = Basin.Effects.CanvasStepPlane.Desktop;
            state.MotionX.Cancel();
            state.MotionY.Cancel();
            _canvasMotions.Remove(window);
            window.MoveTo(targetX, targetY);
            if (window is Window opened && opened.Tree is { } openedTree)
            {
                _effects.OnMapped(openedTree, opened.Rule?.OpenFor(_effects.OpenKind));
            }
        }

        switch (window)
        {
            case Window focusWindow:
                FocusWindow(focusWindow);
                break;
            case XWindow focusX:
                FocusXWindow(focusX);
                break;
        }

        _workspaceModel.RaiseMembersChanged();
        _report.Line($"UNSHELVE id={WindowIdOf(window)} output={view.Output.Name} x={targetX} y={targetY}");
    }

    internal void UnshelveByKey()
    {
        if (ShelvedUnderPointer() is { } under)
        {
            Unshelve(under, null, null);
            return;
        }

        var view = OverviewTargetView();
        if (view is null || OverviewOf(view).Shelved.Count == 0)
        {
            _report.Line("UNSHELVE refused: none");
            return;
        }

        Unshelve(OverviewOf(view).Shelved[^1], null, null);
    }

    private IGrabTarget? _shelveChain;
    private IGrabTarget? _shelveChainFocus;

    private void ShelveFocused(CanvasSide side)
    {
        if (ShelveTarget() is not { } window)
        {
            _report.Line("SHELVE refused: no window");
            return;
        }

        ShelveChained(window, side);
    }

    private void ShelveChained(IGrabTarget window, CanvasSide side)
    {
        Shelve(window, side);
        if (ShelfStateOf(window) is { } shelved && shelved.ShelfSide == side)
        {
            _shelveChain = window;
            _shelveChainFocus = FocusedGrabTarget();
        }
    }

    private IGrabTarget? ShelveTarget() => ShelvedUnderPointer() ?? ShelveChain() ?? FocusedGrabTarget();

    private IGrabTarget? ShelveChain()
    {
        if (_shelveChain is not { } chained)
        {
            return null;
        }

        if (!IsShelved(chained) || chained.EffectTree is not { IsDestroyed: false } ||
            !ReferenceEquals(FocusedGrabTarget(), _shelveChainFocus))
        {
            _shelveChain = null;
            _shelveChainFocus = null;
            return null;
        }

        return chained;
    }

    private IGrabTarget? ShelvedUnderPointer() =>
        ViewAtCursor() is { Tag: OutputPolicy } pointed && OverviewOpenOn(pointed) &&
        _scene.SurfaceAt(_cursorX, _cursorY) is { Surface: { } surface } && CanvasOwnerOf(surface) is { } under &&
        IsShelved(under)
            ? under
            : null;

    private void OverviewMotionDone(IGrabTarget window, CanvasWindowState state)
    {
        if (!state.HideWhenParked)
        {
            return;
        }

        state.HideWhenParked = false;
        if (state.Shelved && state.Owner is { } owner && !OverviewOf(owner).Active)
        {
            ShowShelved(window, shown: false);
        }
    }

    private void OverviewPickup(IGrabTarget window, OutputView view, CanvasWindowState state)
    {
        if (!view.Canvas.Overview)
        {
            return;
        }

        state.HeldIn = state.Shelved;
        state.HeldSide = state.Shelved ? state.ShelfSide : CanvasSide.None;
    }

    private void OverviewDragMotion(IGrabTarget window, OutputView view, CanvasWindowState state, double x, double y)
    {
        if (!view.Canvas.Overview)
        {
            return;
        }

        var settings = OverviewOf(view).Settings;
        var depth = OverviewDepth(view, x, y, out var side);
        var held = OverviewThreshold.Step(state.HeldIn, depth, settings.ThresholdInValue, settings.ThresholdOutValue);
        if (held && side != CanvasSide.None)
        {
            state.HeldSide = side;
        }

        if (held == state.HeldIn)
        {
            return;
        }

        state.HeldIn = held;
        _report.Line($"OVERVIEW held={(held ? "in" : "out")} id={WindowIdOf(window)}");
    }

    private double OverviewDepth(OutputView view, double x, double y, out CanvasSide side)
    {
        if (Stepped(view))
        {
            return StepDepth(view, x, y, out side);
        }

        var canvas = view.Canvas;
        var (warpX, warpY) = canvas.Map.Unzoom(x, y);
        var across = double.NegativeInfinity;
        var along = double.NegativeInfinity;
        var horizontal = CanvasSide.None;
        var vertical = CanvasSide.None;
        foreach (var each in CanvasSides.Horizontal)
        {
            var warp = WarpOf(canvas, each);
            if (warp.IsIdentity)
            {
                continue;
            }

            var depth = warp.Direction * (warpX - warp.Seam) / warp.ZoneWidth;
            if (depth > across)
            {
                across = depth;
                horizontal = each;
            }
        }

        foreach (var each in CanvasSides.Vertical)
        {
            var warp = WarpOf(canvas, each);
            if (warp.IsIdentity)
            {
                continue;
            }

            var depth = warp.Direction * (warpY - warp.Seam) / warp.ZoneWidth;
            if (depth > along)
            {
                along = depth;
                vertical = each;
            }
        }

        if (horizontal == CanvasSide.None && vertical == CanvasSide.None)
        {
            side = CanvasSide.None;
            return double.NegativeInfinity;
        }

        if (across > 0 && along > 0)
        {
            side = horizontal | vertical;
            return OverviewThreshold.CornerDepth(across, along);
        }

        side = across >= along ? horizontal : vertical;
        return Math.Max(across, along);
    }

    private void OverviewDrop(IGrabTarget window)
    {
        if (!_canvasStates.TryGetValue(window, out var state) || window.EffectTree is not { IsDestroyed: false })
        {
            return;
        }

        var owner = state.Shelved ? ShelfOwnerOf(window) : ViewOfWindow(window);
        if (owner is not { Tag: OutputPolicy } view || !view.Canvas.Overview)
        {
            return;
        }

        if (Stepped(view))
        {
            StepDrop(window, view, state);
            return;
        }

        if (ViewAtCursor() is { Tag: OutputPolicy } under && !ReferenceEquals(under, view))
        {
            if (state.Shelved)
            {
                var crossed = CanvasBoxOf(window);
                Unshelve(window, crossed.X, crossed.Y, under);
            }

            return;
        }

        var nanos = Math.Min(DropSettleNanos, CanvasAnimationNanos(view));
        if (state.HeldIn && state.HeldSide != CanvasSide.None && IsOverviewSide(view, state.HeldSide))
        {
            if (!state.Shelved)
            {
                _ = EnterShelf(window, view, state.HeldSide);
                _report.Line($"SHELVE id={WindowIdOf(window)} side={SideNames(state.HeldSide)} output={view.Output.Name}");
            }
            else
            {
                state.ShelfSide = state.HeldSide;
            }

            state.Anchor = null;
            SettleOnShelf(window, view, state, nanos);
            return;
        }

        if (state.Shelved)
        {
            var box = CanvasBoxOf(window);
            Unshelve(window, box.X, box.Y);
        }

        state.Anchor = null;
        SettleOnDesktop(window, view, state, nanos);
    }

    private void SettleOnShelf(IGrabTarget window, OutputView view, CanvasWindowState state, long nanos)
    {
        var canvas = view.Canvas;
        var box = CanvasBoxOf(window);
        var floor = canvas.Settings.ShelfMinScaleValue;
        var targetX = window.X;
        var targetY = window.Y;
        if ((state.ShelfSide & CanvasSide.Horizontal) is var horizontal and not CanvasSide.None)
        {
            targetX = SettleAxis(canvas, WarpOf(canvas, horizontal), box, box.X, box.Width, floor) + (window.X - box.X);
        }

        if ((state.ShelfSide & CanvasSide.Vertical) is var vertical and not CanvasSide.None)
        {
            targetY = SettleAxis(canvas, WarpOf(canvas, vertical), box, box.Y, box.Height, floor) + (window.Y - box.Y);
        }

        BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
        BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
    }

    private static int SettleAxis(CanvasView canvas, CanvasWarp warp, in Box box, int origin, int size, double floor)
    {
        if (warp.IsIdentity)
        {
            return origin;
        }

        var vertical = ReferenceEquals(warp, canvas.Top) || ReferenceEquals(warp, canvas.Bottom);
        var past = vertical ? box with { Y = warp.Direction < 0 ? warp.FarEdge - box.Height : warp.FarEdge }
            : box with { X = warp.Direction < 0 ? warp.FarEdge - box.Width : warp.FarEdge };
        var fit = canvas.Scale.FitFor(canvas.Map, past, floor);
        var park = canvas.Scale.TerraceParkTarget(canvas.Map, warp, box, floor);
        var slack = (int)Math.Floor(size * (1.0 - fit) / 2.0);
        if (warp.Direction > 0)
        {
            var foot = warp.FarEdge - slack;
            return foot > park ? park : Math.Clamp(origin, foot, park);
        }

        var inner = warp.FarEdge - size + slack;
        return inner < park ? park : Math.Clamp(origin, park, inner);
    }

    private void SettleOnDesktop(IGrabTarget window, OutputView view, CanvasWindowState state, long nanos)
    {
        var box = CanvasBoxOf(window);
        var usable = UsableBox(view, _layout.BoxOf(view.Output));
        var left = box.Width >= usable.Width ? usable.X : Math.Clamp(box.X, usable.X, usable.Right - box.Width);
        var top = box.Height >= usable.Height ? usable.Y : Math.Clamp(box.Y, usable.Y, usable.Bottom - box.Height);
        BeginCanvasMotion(window, state, vertical: false, left + (window.X - box.X), nanos);
        BeginCanvasMotion(window, state, vertical: true, top + (window.Y - box.Y), nanos);
    }

    private double _shelfResizeMinX = double.NegativeInfinity;
    private double _shelfResizeMaxX = double.PositiveInfinity;
    private double _shelfResizeMinY = double.NegativeInfinity;
    private double _shelfResizeMaxY = double.PositiveInfinity;

    private const double FootMargin = 0.5;

    private static double ShelfScreen(CanvasView canvas, CanvasWarp warp) => warp.ShelfWidth * canvas.Map.ViewScale;

    private void BeginShelfResize(IGrabTarget window, ResizeEdges edges, double pressX, double pressY)
    {
        _shelfResizeMinX = double.NegativeInfinity;
        _shelfResizeMaxX = double.PositiveInfinity;
        _shelfResizeMinY = double.NegativeInfinity;
        _shelfResizeMaxY = double.PositiveInfinity;
        if (ShelfStateOf(window) is not { } shelved ||
            ViewOfWindow(window) is not { Tag: OutputPolicy, Canvas.Overview: true } view)
        {
            return;
        }

        if (Stepped(view))
        {
            BeginStepShelfResize(window, shelved, view, edges, pressX, pressY);
            return;
        }

        var canvas = view.Canvas;
        var drawn = MapToScreen(window, CanvasBoxOf(window));
        if ((shelved.ShelfSide & CanvasSide.Right) != 0 && !canvas.Right.IsIdentity && (edges & ResizeEdges.Left) != 0)
        {
            var foot = Math.Max(canvas.Map.Zoom(canvas.Right.Foot, 0).X, drawn.Right - ShelfScreen(canvas, canvas.Right));
            _shelfResizeMinX = Math.Min(foot + FootMargin, Math.Min(drawn.X, pressX));
        }

        if ((shelved.ShelfSide & CanvasSide.Left) != 0 && !canvas.Left.IsIdentity && (edges & ResizeEdges.Right) != 0)
        {
            var foot = Math.Min(canvas.Map.Zoom(canvas.Left.Foot, 0).X, drawn.X + ShelfScreen(canvas, canvas.Left));
            _shelfResizeMaxX = Math.Max(foot - FootMargin, Math.Max(drawn.Right, pressX));
        }

        if ((shelved.ShelfSide & CanvasSide.Bottom) != 0 && !canvas.Bottom.IsIdentity && (edges & ResizeEdges.Top) != 0)
        {
            var foot = Math.Max(canvas.Map.Zoom(0, canvas.Bottom.Foot).Y, drawn.Bottom - ShelfScreen(canvas, canvas.Bottom));
            _shelfResizeMinY = Math.Min(foot + FootMargin, Math.Min(drawn.Y, pressY));
        }

        if ((shelved.ShelfSide & CanvasSide.Top) != 0 && !canvas.Top.IsIdentity && (edges & ResizeEdges.Bottom) != 0)
        {
            var foot = Math.Min(canvas.Map.Zoom(0, canvas.Top.Foot).Y, drawn.Y + ShelfScreen(canvas, canvas.Top));
            _shelfResizeMaxY = Math.Max(foot - FootMargin, Math.Max(drawn.Bottom, pressY));
        }
    }

    private Box ClampOverviewResize(IGrabTarget window, Box box, ResizeEdges edges, in Box start)
    {
        if (IsShelved(window) || ViewOfWindow(window) is not { Tag: OutputPolicy, Canvas.Overview: true } view)
        {
            return box;
        }

        var frame = CanvasBoxOf(window);
        var (width, height) = window.GeometrySize;
        var left = window.X - frame.X;
        var top = window.Y - frame.Y;
        var right = frame.Right - (window.X + Math.Max(width, 1));
        var bottom = frame.Bottom - (window.Y + Math.Max(height, 1));
        var usable = UsableBox(view, _layout.BoxOf(view.Output));
        var (minX, maxX, minY, maxY) = (usable.X, usable.Right, usable.Y, usable.Bottom);

        if ((edges & ResizeEdges.Left) != 0)
        {
            var limit = Math.Min(minX, start.X - left);
            if (box.X - left < limit)
            {
                var cut = limit - (box.X - left);
                box = box with { X = box.X + cut, Width = Math.Max(1, box.Width - cut) };
            }
        }

        if ((edges & ResizeEdges.Right) != 0)
        {
            var limit = Math.Max(maxX, start.Right + right);
            if (box.Right + right > limit)
            {
                box = box with { Width = Math.Max(1, box.Width - (box.Right + right - limit)) };
            }
        }

        if ((edges & ResizeEdges.Top) != 0)
        {
            var limit = Math.Min(minY, start.Y - top);
            if (box.Y - top < limit)
            {
                var cut = limit - (box.Y - top);
                box = box with { Y = box.Y + cut, Height = Math.Max(1, box.Height - cut) };
            }
        }

        if ((edges & ResizeEdges.Bottom) != 0)
        {
            var limit = Math.Max(maxY, start.Bottom + bottom);
            if (box.Bottom + bottom > limit)
            {
                box = box with { Height = Math.Max(1, box.Height - (box.Bottom + bottom - limit)) };
            }
        }

        return box;
    }

    private bool OverviewKey(uint key, bool pressed)
    {
        if (key != InputCodes.KeyEsc)
        {
            return false;
        }

        if (!pressed)
        {
            if (!_overviewEscapeHeld)
            {
                return false;
            }

            _overviewEscapeHeld = false;
            return true;
        }

        if (OverviewTargetView() is not { } view || !OverviewOpenOn(view))
        {
            return false;
        }

        _overviewEscapeHeld = true;
        _ = SetOverview(view, false);
        return true;
    }

    private bool OverviewEmptyClick(uint button, bool pressed)
    {
        if (!pressed)
        {
            if (_overviewClickButton != button)
            {
                return false;
            }

            _overviewClickButton = null;
            return true;
        }

        if (_seat.Pointer.HasGrab || ViewAt(_cursorX, _cursorY) is not { Tag: OutputPolicy } view || !OverviewOpenOn(view))
        {
            return false;
        }

        if (_scene.NodeAt(_cursorX, _cursorY) is { Node: { } node })
        {
            SceneNode? walk = node;
            while (walk is not null && !ReferenceEquals(walk, _layers.Background) && !ReferenceEquals(walk, _layers.Bottom))
            {
                walk = walk.Parent;
            }

            if (walk is null)
            {
                return false;
            }
        }

        _overviewClickButton = button;
        _ = SetOverview(view, false);
        return true;
    }

    private void ConfigureOverviewTriggers()
    {
        var fingers = (uint)_config.Overview.FingerCount;
        _overviewTouchGesture.Fingers = fingers;
        for (var i = 0; i < Views.Count; i++)
        {
            if (Views[i].Tag is OutputPolicy)
            {
                var overview = OverviewOf(Views[i]);
                var settings = _config.OverviewFor(Views[i].Output.Name);
                overview.Corner.Corner = settings.HotCornerValue;
                overview.Corner.DelayMs = (uint)settings.HotCornerMillis;
                overview.Corner.Reset();
            }
        }
    }

    private bool BeginOverviewSwipe(uint fingers, uint timeMs) =>
        BeginOverviewSwipeAt(ViewAtCursor(), OverviewSwipeTravel, SwipeFlingPerSecond, fingers, timeMs);

    private const double OverviewSwipeTravel = 400;

    private bool BeginOverviewSwipeAt(OutputView? at, double travel, double fling, uint fingers, uint timeMs)
    {
        if (at is not { Tag: OutputPolicy } view || !_config.OverviewFor(view.Output.Name).GestureEnabled ||
            fingers != (uint)_config.Overview.FingerCount)
        {
            return false;
        }

        var overview = OverviewOf(view);
        var opening = !overview.Open;
        if (opening && OverviewRefusal(view) is not null)
        {
            return false;
        }

        if (_mode != DragMode.None || _effects.SwitcherActive)
        {
            return false;
        }

        var recognizer = RecognizerFor(fingers);
        if (!recognizer.Begin(fingers, travel, timeMs))
        {
            return false;
        }

        recognizer.FlingPerSecond = fling;
        recognizer.ClampHigh = opening;
        recognizer.ClampLow = !opening;
        _overviewSwipeView = view;
        _overviewSwipeOpening = opening;
        return true;
    }

    private SwipeRecognizer RecognizerFor(uint fingers)
    {
        if (_overviewSwipeRecognizer.Fingers != fingers)
        {
            _overviewSwipeRecognizer = new SwipeRecognizer(fingers) { Axis = SwipeAxis.Vertical };
        }

        return _overviewSwipeRecognizer;
    }

    private SwipeRecognizer _overviewSwipeRecognizer = new(OverviewSetting.DefaultFingers) { Axis = SwipeAxis.Vertical };

    private bool UpdateOverviewSwipe(double dx, double dy, uint timeMs)
    {
        var recognizer = _overviewSwipeRecognizer;
        if (!recognizer.Update(dx, dy, timeMs))
        {
            return false;
        }

        if (_overviewSwipeView is not { } view)
        {
            return true;
        }

        var overview = OverviewOf(view);
        var progress = recognizer.Progress;
        var travel = _overviewSwipeOpening ? Math.Clamp(-progress, 0.0, 1.0) : Math.Clamp(progress, 0.0, 1.0);
        if (!overview.Tracking && travel > 0)
        {
            if (_overviewSwipeOpening)
            {
                EnterOverview(view);
            }

            overview.Tracking = true;
            overview.Progress.Cancel();
        }

        if (!overview.Tracking)
        {
            return true;
        }

        overview.Value = _overviewSwipeOpening ? travel : 1.0 - travel;
        LayoutCanvas(view, animating: true);
        return true;
    }

    private bool EndOverviewSwipe(bool cancelled, uint timeMs)
    {
        var outcome = _overviewSwipeRecognizer.End(cancelled, timeMs);
        if (outcome == SwipeOutcome.None)
        {
            return false;
        }

        var view = _overviewSwipeView;
        var opening = _overviewSwipeOpening;
        _overviewSwipeView = null;
        if (view is null)
        {
            return true;
        }

        var overview = OverviewOf(view);
        var tracked = overview.Tracking;
        overview.Tracking = false;
        var open = outcome == SwipeOutcome.Commit ? opening : !opening;
        if (!tracked && outcome != SwipeOutcome.Commit)
        {
            return true;
        }

        if (overview.Open == open)
        {
            overview.Progress.Begin(overview.Value, open ? 1.0 : 0.0, CanvasAnimationNanos(view));
            if (!overview.Progress.IsRunning)
            {
                overview.Value = open ? 1.0 : 0.0;
                FinishOverview(view);
            }

            ScheduleEffectRepaint();
            return true;
        }

        _ = SetOverview(view, open);
        return true;
    }

    private readonly CentroidSwipeGesture _overviewTouchGesture = new()
    {
        Fingers = OverviewSetting.DefaultFingers,
        Slop = TouchSwipeSlop,
        Axis = SwipeAxis.Vertical,
    };

    private sealed class OverviewTouchHandler(TinyComp comp) : ICentroidSwipeHandler
    {
        public bool Begin(double centroidX, double centroidY, uint timeMs)
        {
            if (comp._touchFramePress is not null || comp._touchMoveResize is { Dragging: true })
            {
                return false;
            }

            var view = comp.ViewAt(centroidX, centroidY);
            var travel = view is null ? 0 : comp._layout.BoxOf(view.Output).Height;
            return travel > 0 &&
                comp.BeginOverviewSwipeAt(view, travel, travel * TouchFlingFraction, comp._overviewTouchGesture.Fingers, timeMs);
        }

        public void Update(double dx, double dy, uint timeMs) => _ = comp.UpdateOverviewSwipe(dx, dy, timeMs);

        public void End(bool cancelled, uint timeMs) => _ = comp.EndOverviewSwipe(cancelled, timeMs);
    }

    private void TrackHotCorner(double x, double y, uint timeMs)
    {
        if (_mode != DragMode.None || _seat.Pointer.HasImplicitGrab || _sessionLock.IsLocked ||
            ViewAt(x, y) is not { Tag: OutputPolicy } view)
        {
            ResetHotCorners();
            return;
        }

        var corner = OverviewOf(view).Corner;
        if (!_config.OverviewFor(view.Output.Name).Enabled)
        {
            corner.Reset();
            return;
        }

        var armed = corner.IsArmed;
        if (corner.Motion(_layout.BoxOf(view.Output), x, y, timeMs))
        {
            FireHotCorner(view);
            return;
        }

        if (!armed && corner.IsArmed)
        {
            _hotCornerView = view;
            _hotCornerTimer ??= _loop.AddTimer(OnHotCornerTimer);
            _hotCornerTimer.UpdateTimer((int)Math.Max(1, corner.DelayMs));
        }
    }

    private void ResetHotCorners()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            if (Views[i].Tag is OutputPolicy)
            {
                OverviewOf(Views[i]).Corner.Reset();
            }
        }
    }

    private void OnHotCornerTimer()
    {
        if (_hotCornerView is not { Tag: OutputPolicy } view || !Views.Contains(view))
        {
            return;
        }

        var corner = OverviewOf(view).Corner;
        if (corner.IsArmed && _mode == DragMode.None && !_seat.Pointer.HasImplicitGrab && corner.Poll(corner.DueMs))
        {
            FireHotCorner(view);
        }
    }

    private void FireHotCorner(OutputView view)
    {
        _report.Line($"HOTCORNER output={view.Output.Name} corner={OverviewSetting.NameOf(OverviewOf(view).Corner.Corner)}");
        _ = SetOverview(view, !OverviewOf(view).Open);
    }

    private void UnshelveFromOutput(OutputView gone, OutputView? fallback)
    {
        var overview = OverviewOf(gone);
        for (var i = overview.Shelved.Count - 1; i >= 0; i--)
        {
            var window = overview.Shelved[i];
            if (fallback is null)
            {
                if (_canvasStates.TryGetValue(window, out var state))
                {
                    state.Shelved = false;
                    state.Owner = null;
                }

                ShowShelved(window, shown: true);
                continue;
            }

            var sourceBox = _layout.BoxOf(gone.Output);
            var targetBox = _layout.BoxOf(fallback.Output);
            var pre = _canvasStates.TryGetValue(window, out var shelved) ? shelved : null;
            var box = CanvasBoxOf(window);
            var x = pre is null ? box.X : pre.PreShelfX - (window.X - box.X) - sourceBox.X + targetBox.X;
            var y = pre is null ? box.Y : pre.PreShelfY - (window.Y - box.Y) - sourceBox.Y + targetBox.Y;
            Unshelve(window, x, y, fallback);
        }

        overview.Shelved.Clear();
        overview.Open = false;
        overview.Tracking = false;
        overview.Progress.Cancel();
        overview.Value = 0;
        overview.ShelfTree?.Destroy();
        overview.ShelfTree = null;
        overview.BackgroundZoom?.Destroy();
        overview.BackgroundZoom = null;
        overview.BottomZoom?.Destroy();
        overview.BottomZoom = null;
        LeaveStep(gone);
    }

    private void ForgetShelf(IGrabTarget window)
    {
        if (ReferenceEquals(_shelveChain, window) || ReferenceEquals(_shelveChainFocus, window))
        {
            _shelveChain = null;
            _shelveChainFocus = null;
        }

        for (var i = 0; i < Views.Count; i++)
        {
            if (Views[i].Tag is OutputPolicy)
            {
                _ = OverviewOf(Views[i]).Shelved.Remove(window);
            }
        }
    }

    private string OverviewWhere(IGrabTarget window)
    {
        var state = _canvasStates.TryGetValue(window, out var known) ? known : null;
        var region = state?.Region switch
        {
            CanvasRegion.Slope => "slope",
            CanvasRegion.Shelf => "shelf",
            CanvasRegion.Corner => "corner",
            _ => "flat",
        };
        var shelved = state?.Shelved == true;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"OVERVIEWWIN id={WindowIdOf(window)} shelved={(shelved ? "true" : "false")}"
            + $" side={(shelved ? SideNames(state!.ShelfSide) : "none")} region={region}"
            + $" k={state?.Scale ?? 1.0:F2} fit={state?.Fit ?? 1.0:F2}");
    }

    private void WriteShelvesJson(System.Text.Json.Utf8JsonWriter json, OutputView? only)
    {
        json.WriteStartArray();
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is not OutputPolicy || (only is not null && !ReferenceEquals(view, only)))
            {
                continue;
            }

            foreach (var window in OverviewOf(view).Shelved)
            {
                var state = _canvasStates.TryGetValue(window, out var known) ? known : null;
                json.WriteStartObject();
                json.WriteString("id", WindowIdOf(window));
                json.WriteString("output", view.Output.Name);
                json.WriteString("side", state is null ? "none" : SideNames(state.ShelfSide));
                json.WriteNumber("x", window.X);
                json.WriteNumber("y", window.Y);
                json.WriteNumber("k", state?.Scale ?? 1.0);
                json.WriteNumber("fit", state?.Fit ?? 1.0);
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
    }

    private void ReportShelvedWindows()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is not OutputPolicy)
            {
                continue;
            }

            foreach (var window in OverviewOf(view).Shelved)
            {
                var state = _canvasStates.TryGetValue(window, out var known) ? known : null;
                _report.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"SHELF id={WindowIdOf(window)} output={view.Output.Name} side={(state is null ? "none" : SideNames(state.ShelfSide))}"
                    + $" x={window.X} y={window.Y} k={state?.Scale ?? 1.0:F2} fit={state?.Fit ?? 1.0:F2}"));
            }
        }
    }
}
