using Basin;
using Basin.Effects;
using Basin.Host;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private static CanvasStepSides StepSides(CanvasSide side) => (CanvasStepSides)(int)side;

    private static bool Stepped(OutputView view) => view.Canvas.Step is not null;

    private void LayoutStepSides(OutputView view, in Box box, in Box usable)
    {
        var overview = OverviewOf(view);
        var settings = view.Canvas.Settings;
        var scale = overview.Settings.ScaleValue;
        var wall = overview.Settings.WallWidthValue;
        var active = CanvasSide.None;
        var border = CanvasSide.None;
        foreach (var side in CanvasSides.Each)
        {
            var index = SideIndex(side);
            var (outer, edge, center, size) = SideFrame(side, box, usable);
            var wanted = settings.SideSet.HasFlag(side) && !HasNeighbour(view.Output, box, side);
            var full = wanted
                ? OverviewLayout.StepFull(outer, edge, center, Direction(side), scale, wall, size)
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
            overview.OuterCanvas[index] = double.NaN;
        }

        overview.Sides = active;
        overview.BorderSides = border;
    }

    private double StepShelfScale(OutputView view, bool? animate)
    {
        var overview = OverviewOf(view);
        var canvas = view.Canvas;
        var scale = overview.Settings.ScaleValue;
        var shelf = double.PositiveInfinity;
        foreach (var side in CanvasSides.Each)
        {
            if ((overview.Sides & side) == CanvasSide.None)
            {
                continue;
            }

            var value = animate is { } animated
                ? ShelfScaleFor(view, side, canvas.Settings, animated, pin: false)
                : ShelfScaleInForce(canvas, side);
            shelf = Math.Min(shelf, value);
        }

        if (double.IsPositiveInfinity(shelf))
        {
            shelf = ShelfScaleInForce(canvas, CanvasSide.Right);
        }

        return Math.Min(shelf, scale);
    }

    private void SetStepFull(OutputView view, in Box box, in Box usable, double shelfScale)
    {
        var overview = OverviewOf(view);
        var full = overview.StepFull;
        _ = OverviewLayout.LayoutStep(full, box, usable, overview.FullSides, 1.0, overview.Settings.ScaleValue, shelfScale);
        var seen = overview.StepSeen;
        if (overview.StepSeenValid && !SameStep(seen, full))
        {
            FollowStepShelves(view, seen, full);
        }

        _ = seen.Layout(full.CenterX, full.CenterY, full.Zoom, full.ShelfScale, full.Outline, full.Inner, full.Outer, full.Sides);
        overview.StepSeenValid = true;
    }

    private static bool SameStep(CanvasStepMap a, CanvasStepMap b) =>
        a.CenterX == b.CenterX && a.CenterY == b.CenterY && a.Zoom == b.Zoom && a.ShelfScale == b.ShelfScale &&
        a.Outline == b.Outline && a.Inner == b.Inner && a.Outer == b.Outer && a.Sides == b.Sides;

    private static (double X, double Y, double HalfWidth, double HalfHeight) StepDrawn(
        CanvasStepMap map, in Box box, CanvasStepSides side, double floor)
    {
        var fit = map.ShelfFit(box.Width, box.Height, side, floor);
        var (x, y) = map.ToScreen(CanvasStepPlane.Shelf, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        var scale = map.ShelfScale * fit;
        return (x, y, box.Width * scale / 2.0, box.Height * scale / 2.0);
    }

    private static double StepAxis(double center, double low, double high, double half, int outer, bool pin)
    {
        if (high - low <= 2.0 * half)
        {
            return outer > 0 ? high - half : low + half;
        }

        if (pin && outer != 0)
        {
            return outer > 0 ? high - half : low + half;
        }

        return Math.Clamp(center, low + half, high - half);
    }

    private static int OuterOf(CanvasStepSides side, bool horizontal) => horizontal
        ? (side & CanvasStepSides.Right) != 0 ? 1 : (side & CanvasStepSides.Left) != 0 ? -1 : 0
        : (side & CanvasStepSides.Bottom) != 0 ? 1 : (side & CanvasStepSides.Top) != 0 ? -1 : 0;

    private (int X, int Y) StepShelfTarget(
        OutputView view, IGrabTarget window, in Box box, CanvasSide side, double drawnX, double drawnY, bool pin)
    {
        var full = OverviewOf(view).StepFull;
        var sides = StepSides(side);
        var (_, _, halfWidth, halfHeight) = StepDrawn(full, box, sides, view.Canvas.Settings.ShelfMinScaleValue);
        var strip = full.ShelfStrip(sides);
        var x = StepAxis(drawnX, strip.X, strip.Right, halfWidth, OuterOf(sides, horizontal: true), pin);
        var y = StepAxis(drawnY, strip.Y, strip.Bottom, halfHeight, OuterOf(sides, horizontal: false), pin);
        var (canvasX, canvasY) = full.ToCanvas(CanvasStepPlane.Shelf, x, y);
        return (
            (int)Math.Round(canvasX - (box.Width / 2.0)) + (window.X - box.X),
            (int)Math.Round(canvasY - (box.Height / 2.0)) + (window.Y - box.Y));
    }

    private void FollowStepShelves(OutputView view, CanvasStepMap old, CanvasStepMap now)
    {
        var overview = OverviewOf(view);
        var floor = view.Canvas.Settings.ShelfMinScaleValue;
        var pinning = view.Canvas.ShelfAnimating;
        foreach (var window in overview.Shelved)
        {
            if (!_canvasStates.TryGetValue(window, out var state) || state.Dragging || state.Resizing ||
                state.MotionX.IsRunning || state.MotionY.IsRunning)
            {
                continue;
            }

            var box = CanvasBoxOf(window);
            var side = StepSides(state.ShelfSide);
            var before = old.ShelfStrip(side);
            var after = now.ShelfStrip(side);
            if (before.IsEmpty || after.IsEmpty)
            {
                continue;
            }

            if (!state.ShelfPinned || state.PinSide != state.ShelfSide)
            {
                var (x, y, halfWidth, halfHeight) = StepDrawn(old, box, side, floor);
                state.PinSide = state.ShelfSide;
                state.PinOuter = OuterOf(side, horizontal: true) switch
                {
                    > 0 => before.Right - (x + halfWidth),
                    < 0 => (x - halfWidth) - before.X,
                    _ => x,
                };
                state.PinCenter = OuterOf(side, horizontal: false) switch
                {
                    > 0 => before.Bottom - (y + halfHeight),
                    < 0 => (y - halfHeight) - before.Y,
                    _ => y,
                };
                state.ShelfPinned = pinning;
            }

            var (_, _, width, height) = StepDrawn(now, box, side, floor);
            var drawnX = OuterOf(side, horizontal: true) switch
            {
                > 0 => after.Right - state.PinOuter - width,
                < 0 => after.X + state.PinOuter + width,
                _ => StepAxis(state.PinOuter, after.X, after.Right, width, 0, pin: false),
            };
            var drawnY = OuterOf(side, horizontal: false) switch
            {
                > 0 => after.Bottom - state.PinCenter - height,
                < 0 => after.Y + state.PinCenter + height,
                _ => StepAxis(state.PinCenter, after.Y, after.Bottom, height, 0, pin: false),
            };
            var (canvasX, canvasY) = now.ToCanvas(CanvasStepPlane.Shelf, drawnX, drawnY);
            var movedX = (int)Math.Round(canvasX - (box.Width / 2.0)) + (window.X - box.X);
            var movedY = (int)Math.Round(canvasY - (box.Height / 2.0)) + (window.Y - box.Y);
            if (movedX != window.X || movedY != window.Y)
            {
                window.MoveTo(movedX, movedY);
            }
        }
    }

    private void ReshelveAll(OutputView view)
    {
        var overview = OverviewOf(view);
        var steps = overview.Settings.Steps;
        foreach (var window in overview.Shelved)
        {
            if (!_canvasStates.TryGetValue(window, out var state) || window.EffectTree is not { IsDestroyed: false })
            {
                continue;
            }

            state.MotionX.Cancel();
            state.MotionY.Cancel();
            state.ShelfPinned = false;
            var side = state.ShelfSide;
            if (steps && (side & CanvasSide.Horizontal) != 0 && (side & CanvasSide.Vertical) != 0 &&
                OverviewOf(view).StepFull.ShelfStrip(StepSides(side)).IsEmpty)
            {
                side &= CanvasSide.Horizontal;
            }

            if ((overview.Sides & side) != side)
            {
                continue;
            }

            state.ShelfSide = side;
            var box = CanvasBoxOf(window);
            var (drawnX, drawnY) = overview.StepFull.ToScreen(
                CanvasStepPlane.Shelf, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            var (x, y) = steps
                ? StepShelfTarget(view, window, box, side, drawnX, drawnY, pin: true)
                : SlopeShelfTarget(view, window, box, side);
            state.StepPlane = steps ? CanvasStepPlane.Shelf : CanvasStepPlane.Desktop;
            state.StepView = steps ? view : null;
            if (x != window.X || y != window.Y)
            {
                window.MoveTo(x, y);
            }
        }
    }

    private void LayoutOverviewStep(OutputView view, in Box box, bool animating)
    {
        var canvas = view.Canvas;
        var overview = OverviewOf(view);
        var settings = canvas.Settings;
        var scale = overview.Settings.ScaleValue;
        var usable = UsableBox(view, box);
        var centerX = box.X + (box.Width / 2.0);
        var centerY = box.Y + (box.Height / 2.0);
        var wasOverview = canvas.Overview;
        var changed = !wasOverview || canvas.Step is null;
        canvas.Overview = true;
        if (canvas.Mode != CanvasWindowMode.Terrace)
        {
            canvas.Mode = CanvasWindowMode.Terrace;
            changed = true;
        }

        foreach (var side in CanvasSides.Each)
        {
            var (outer, _, _, _) = SideFrame(side, box, usable);
            var fan = (side & CanvasSide.Horizontal) != 0 ? centerY : centerX;
            changed |= WarpOf(canvas, side).LayoutTerrace(
                outer, Direction(side), 0, 0, 1.0, CanvasWarp.TerraceExponent, settings.SlopeValue, fan);
        }

        if (canvas.Map.ViewScale != 1.0)
        {
            canvas.Map.ViewScale = 1.0;
            changed = true;
        }

        changed |= LayoutCanvasGrid(view, box, wanted: false);
        var shelfScale = StepShelfScale(view, wasOverview && !animating);
        SetStepFull(view, box, usable, shelfScale);
        changed |= OverviewLayout.LayoutStep(overview.Step, box, usable, overview.FullSides, overview.Value, scale, shelfScale);
        canvas.Step = overview.Step;
        var meshChanged = LayoutStepMesh(view, box);
        SyncLayerZoom(view, overview.Step.Zoom, centerX, centerY);
        if (!changed && !meshChanged)
        {
            return;
        }

        if (changed)
        {
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

            ApplyCanvasToView(view);
        }

        overview.StepMesh?.NotifyMeshChanged();
        view.Scheduler?.ScheduleRepaint();
    }

    private bool LayoutStepMesh(OutputView view, in Box box)
    {
        var overview = OverviewOf(view);
        var canvas = view.Canvas;
        var settings = canvas.Settings;
        var source = overview.StepSource;
        var color = settings.GridRenderColor;
        var wall = overview.Settings.WallRenderColor;
        var alpha = GridAlphaFor(settings) * (float)overview.Value;
        var lines = settings.GridMode != CanvasGridMode.Never;
        var changed = !ReferenceEquals(source.Map, overview.Step) || source.CellSize != settings.GridCellSize ||
            source.Color != color || source.WallColor != wall || source.WallShade != overview.Settings.WallShadeValue ||
            source.Alpha != alpha || source.Lines != lines;
        source.Map = overview.Step;
        source.CellSize = settings.GridCellSize;
        source.Color = color;
        source.WallColor = wall;
        source.WallShade = overview.Settings.WallShadeValue;
        source.Alpha = alpha;
        source.Lines = lines;
        if (overview.StepMesh is not { IsDestroyed: false } mesh)
        {
            overview.StepMesh = new SceneMesh(_layers.Background) { Source = source, Bounds = box };
            return true;
        }

        changed |= mesh.Bounds != box;
        mesh.Bounds = box;
        return changed;
    }

    private void LeaveStep(OutputView view)
    {
        var overview = OverviewOf(view);
        view.Canvas.Step = null;
        overview.StepMesh?.Destroy();
        overview.StepMesh = null;
    }

    private void ApplyOverviewStep(IGrabTarget window, SceneTree tree, OutputView view, CanvasWindowState? state)
    {
        var canvas = view.Canvas;
        var overview = OverviewOf(view);
        var map = overview.Step;
        if (state is null)
        {
            if (map.IsIdentity)
            {
                return;
            }

            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var box = CanvasBoxOf(window);
        var plane = state.Shelved || state.Dragging ? state.StepPlane : CanvasStepPlane.Desktop;
        var side = StepSides(state.ShelfSide);
        var fit = 1.0;
        RenderTransform target;
        if (_canvasSuspended)
        {
            target = RenderTransform.Identity;
        }
        else if (state.Resizing)
        {
            var start = state.ResizeStart;
            var fixedX = state.ResizeFixedLeft ? start.Right : start.X;
            var fixedY = state.ResizeFixedTop ? start.Bottom : start.Y;
            target = CanvasScale.About(state.ResizeScale, fixedX, fixedY, state.ResizeScreenX, state.ResizeScreenY);
        }
        else if (state.Dragging)
        {
            target = CanvasScale.About(
                map.DragScale(state.StepDepth), window.X + state.DragGrabX, window.Y + state.DragGrabY,
                state.DragCursorX, state.DragCursorY);
        }
        else
        {
            if (plane == CanvasStepPlane.Shelf)
            {
                fit = 1.0 + ((overview.StepFull.ShelfFit(box.Width, box.Height, side, canvas.Settings.ShelfMinScaleValue) - 1.0) *
                    overview.Value);
            }

            target = map.Placement(plane, fit, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            if (!target.IsIdentity && !IsTerraceSettling(state))
            {
                target = SnapPlacement(window, target);
            }
        }

        var placement = state.Blend.IsRunning ? CanvasScale.Blend(state.BlendFrom, target, state.Blend.Current) : target;
        var node = state.Node;
        if (node is not null && !node.IsDestroyed)
        {
            AdoptStrays(tree, node);
            node.Deformer = null;
            SetCanvasMatrix(node, tree, placement);
        }
        else if (!placement.IsIdentity)
        {
            node = EnsureCanvasNode(tree, state);
            AdoptStrays(tree, node);
            node.Deformer = null;
            SetCanvasMatrix(node, tree, placement);
        }

        state.Region = plane == CanvasStepPlane.Desktop || state.Dragging
            ? CanvasRegion.Flat
            : (side & CanvasStepSides.Horizontal) != 0 && (side & CanvasStepSides.Vertical) != 0 ? CanvasRegion.Corner : CanvasRegion.Shelf;
        state.Fit = fit;
        state.Placement = placement;
        state.Scale = placement.IsIdentity ? 1.0 : placement.M11;
        state.Deformed = !placement.IsIdentity;
        state.AppliedGeneration = canvas.Generation;
        state.AppliedSceneX = tree.X;
        state.AppliedSceneY = tree.Y;
        if (_overrideRedirects.Count > 0)
        {
            SyncOverrideRedirects(window, placement);
        }

        SyncScaleOffer(window, state, canvas);
    }

    private void BeginStepGrab(IGrabTarget window, OutputView view, double x, double y)
    {
        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        state.Blend.Cancel();
        state.MotionX.Cancel();
        state.MotionY.Cancel();
        if (!state.Placement.IsIdentity)
        {
            state.BlendFrom = state.Placement;
            state.Blend.Begin(0.0, 1.0, Math.Min(DropSettleNanos, CanvasAnimationNanos(view)));
            if (state.Blend.IsRunning && !_canvasMotions.Contains(window))
            {
                _canvasMotions.Add(window);
                ScheduleEffectRepaint();
            }
        }

        state.StepPickup = state.Shelved ? null : (window.X, window.Y);
        state.DragGrabX = _grabX;
        state.DragGrabY = _grabY;
        state.DragStartX = x;
        state.DragStartY = y;
        state.Anchor = null;
        state.ShelfPinned = false;
        state.Dragging = true;
        _canvasScaleDrag = true;
        _ = DragStep(window, view, state, x, y);
        OverviewPickup(window, view, state);
    }

    private bool DragStep(IGrabTarget window, OutputView view, CanvasWindowState state, double x, double y)
    {
        var map = OverviewOf(view).Step;
        var (fieldX, fieldY) = map.ToCanvasNearest(x, y, out var plane);
        var crossed = plane != state.StepPlane;
        state.StepDepth = map.WallDepth(x, y, out _);
        state.StepPlane = plane;
        state.StepView = view;
        var movedX = x - state.DragCursorX;
        var movedY = y - state.DragCursorY;
        state.DragCursorX = x;
        state.DragCursorY = y;
        state.DragFieldX = fieldX;
        state.DragFieldY = fieldY;
        var nextX = (int)Math.Round(fieldX - state.DragGrabX);
        var nextY = (int)Math.Round(fieldY - state.DragGrabY);
        if (nextX != window.X || nextY != window.Y)
        {
            if (crossed)
            {
                MoveHoldingBlend(window, state, nextX, nextY);
            }
            else
            {
                window.MoveTo(nextX, nextY);
            }
        }
        else
        {
            ApplyCanvas(window);
        }

        return movedX != 0 || movedY != 0;
    }

    private void StepDrop(IGrabTarget window, OutputView view, CanvasWindowState state)
    {
        var map = OverviewOf(view).Step;
        var nanos = Math.Min(DropSettleNanos, CanvasAnimationNanos(view));
        if (ViewAtCursor() is { Tag: OutputPolicy } under && !ReferenceEquals(under, view))
        {
            var drawn = MapToScreen(window, CanvasBoxOf(window));
            var (landX, landY) = under.Canvas.ToCanvasPoint(drawn.X, drawn.Y);
            var box = CanvasBoxOf(window);
            state.StepPlane = CanvasStepPlane.Desktop;
            state.StepView = null;
            if (state.Shelved)
            {
                Unshelve(window, landX, landY, under);
                return;
            }

            window.MoveTo((int)Math.Round(landX) + (window.X - box.X), (int)Math.Round(landY) + (window.Y - box.Y));
            return;
        }

        var inward = state.HeldIn && state.HeldSide != CanvasSide.None && IsOverviewSide(view, state.HeldSide);
        var plane = inward ? CanvasStepPlane.Shelf : CanvasStepPlane.Desktop;
        var (fieldX, fieldY) = map.ToCanvas(plane, state.DragCursorX, state.DragCursorY);
        state.StepPlane = plane;
        state.StepView = inward ? view : null;
        var landedX = (int)Math.Round(fieldX - state.DragGrabX);
        var landedY = (int)Math.Round(fieldY - state.DragGrabY);
        if (landedX != window.X || landedY != window.Y)
        {
            MoveHoldingBlend(window, state, landedX, landedY);
        }

        if (inward)
        {
            if (!state.Shelved)
            {
                _ = EnterShelf(window, view, state.HeldSide);
                if (state.StepPickup is { } pickup)
                {
                    state.PreShelfX = pickup.X;
                    state.PreShelfY = pickup.Y;
                }

                _report.Line($"SHELVE id={WindowIdOf(window)} side={SideNames(state.HeldSide)} output={view.Output.Name}");
            }
            else
            {
                state.ShelfSide = state.HeldSide;
            }

            state.StepPlane = CanvasStepPlane.Shelf;
            state.StepView = view;
            state.Anchor = null;
            var box = CanvasBoxOf(window);
            var full = OverviewOf(view).StepFull;
            var (drawnX, drawnY) = full.ToScreen(CanvasStepPlane.Shelf, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            var (targetX, targetY) = StepShelfTarget(view, window, box, state.ShelfSide, drawnX, drawnY, pin: false);
            BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
            BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
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

    private void StepShelve(IGrabTarget window, OutputView view, CanvasSide side)
    {
        var overview = OverviewOf(view);
        var closed = !overview.Active;
        var box = CanvasBoxOf(window);
        var (drawnX, drawnY) = overview.StepFull.ToScreen(
            CanvasStepPlane.Desktop, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        if (ShelfStateOf(window) is { } shelved)
        {
            var (shelfX, shelfY) = overview.StepFull.ToScreen(
                CanvasStepPlane.Shelf, box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            var (homeX, homeY) = overview.StepFull.ToScreen(
                CanvasStepPlane.Desktop,
                shelved.PreShelfX - (window.X - box.X) + (box.Width / 2.0),
                shelved.PreShelfY - (window.Y - box.Y) + (box.Height / 2.0));
            var kept = ShelfAlongKept(shelved.ShelfSide, side);
            drawnX = kept ? shelfX : homeX;
            drawnY = kept ? shelfY : homeY;
        }

        var state = EnterShelf(window, view, side);
        var (targetX, targetY) = StepShelfTarget(view, window, box, side, drawnX, drawnY, pin: true);
        state.StepView = view;
        var nanos = CanvasAnimationNanos(view);
        if (closed)
        {
            state.StepPlane = CanvasStepPlane.Shelf;
            state.HideWhenParked = true;
            BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
            BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
            if (!state.MotionX.IsRunning && !state.MotionY.IsRunning)
            {
                state.HideWhenParked = false;
                ShowShelved(window, shown: false);
            }

            return;
        }

        JumpWithBlend(window, view, state, CanvasStepPlane.Shelf, targetX, targetY);
    }

    private void JumpWithBlend(IGrabTarget window, OutputView view, CanvasWindowState state, CanvasStepPlane plane, int x, int y)
    {
        state.MotionX.Cancel();
        state.MotionY.Cancel();
        state.BlendFrom = state.Placement;
        state.StepPlane = plane;
        state.Blend.Cancel();
        state.Blend.Begin(0.0, 1.0, CanvasAnimationNanos(view));
        if (state.Blend.IsRunning && !_canvasMotions.Contains(window))
        {
            _canvasMotions.Add(window);
            ScheduleEffectRepaint();
        }

        if (x != window.X || y != window.Y)
        {
            MoveHoldingBlend(window, state, x, y);
        }
        else
        {
            ApplyCanvas(window);
        }
    }

    private static void MoveHoldingBlend(IGrabTarget window, CanvasWindowState state, int x, int y)
    {
        if (state.Blend.IsRunning)
        {
            state.BlendFrom = RenderTransform.Multiply(state.BlendFrom, RenderTransform.Translation(window.X - x, window.Y - y));
        }

        window.MoveTo(x, y);
    }

    private void BeginStepShelfResize(IGrabTarget window, CanvasWindowState shelved, OutputView view, ResizeEdges edges, double pressX, double pressY)
    {
        var map = OverviewOf(view).Step;
        var side = StepSides(shelved.ShelfSide);
        var strip = map.ShelfStrip(side);
        if (strip.IsEmpty)
        {
            return;
        }

        var drawn = MapToScreen(window, CanvasBoxOf(window));
        if ((side & CanvasStepSides.Right) != 0 && (edges & ResizeEdges.Left) != 0)
        {
            var foot = Math.Max(strip.X, drawn.Right - strip.Width);
            _shelfResizeMinX = Math.Min(foot + FootMargin, Math.Min(drawn.X, pressX));
        }

        if ((side & CanvasStepSides.Left) != 0 && (edges & ResizeEdges.Right) != 0)
        {
            var foot = Math.Min(strip.Right, drawn.X + strip.Width);
            _shelfResizeMaxX = Math.Max(foot - FootMargin, Math.Max(drawn.Right, pressX));
        }

        if ((side & CanvasStepSides.Bottom) != 0 && (edges & ResizeEdges.Top) != 0)
        {
            var foot = Math.Max(strip.Y, drawn.Bottom - strip.Height);
            _shelfResizeMinY = Math.Min(foot + FootMargin, Math.Min(drawn.Y, pressY));
        }

        if ((side & CanvasStepSides.Top) != 0 && (edges & ResizeEdges.Bottom) != 0)
        {
            var foot = Math.Min(strip.Bottom, drawn.Y + strip.Height);
            _shelfResizeMaxY = Math.Max(foot - FootMargin, Math.Max(drawn.Bottom, pressY));
        }
    }

    private double StepDepth(OutputView view, double x, double y, out CanvasSide side)
    {
        var depth = OverviewOf(view).Step.WallDepth(x, y, out var sides);
        side = (CanvasSide)(int)sides;
        return depth;
    }
}
