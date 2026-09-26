using System.Globalization;
using Basin;
using Basin.Effects;
using Basin.Host;
using Basin.Scene;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const double TerraceGridSpacing = 8.0;

    private static int SideIndex(CanvasSide side) => side switch
    {
        CanvasSide.Left => 0,
        CanvasSide.Right => 1,
        CanvasSide.Top => 2,
        _ => 3,
    };

    private static CanvasWarp WarpOf(CanvasView canvas, CanvasSide side) => side switch
    {
        CanvasSide.Left => canvas.Left,
        CanvasSide.Right => canvas.Right,
        CanvasSide.Top => canvas.Top,
        _ => canvas.Bottom,
    };

    private static int SpanEdge(CanvasWarp warp) =>
        warp.Terrace ? (int)Math.Round(warp.ToCanvas(warp.OuterEdge)) : warp.FarEdge;

    private static string ShelfScaleNames(CanvasView canvas) => string.Create(
        CultureInfo.InvariantCulture,
        $"{ShelfScaleInForce(canvas, CanvasSide.Left):F2},{ShelfScaleInForce(canvas, CanvasSide.Right):F2},"
        + $"{ShelfScaleInForce(canvas, CanvasSide.Top):F2},{ShelfScaleInForce(canvas, CanvasSide.Bottom):F2}");

    private static double ShelfScaleInForce(CanvasView canvas, CanvasSide side)
    {
        var index = SideIndex(side);
        return !double.IsNaN(canvas.ShelfOverride[index]) ? canvas.ShelfOverride[index] : canvas.Settings.ShelfScaleValues.For(side);
    }

    private static void ForgetStaleOverrides(CanvasView canvas, CanvasSetting previous, CanvasSetting settings)
    {
        if (previous.WindowMode != settings.WindowMode)
        {
            canvas.ModeOverride = null;
        }

        var before = previous.ShelfScaleValues;
        var after = settings.ShelfScaleValues;
        foreach (var side in CanvasSides.Each)
        {
            if (before.For(side) != after.For(side))
            {
                canvas.ShelfOverride[SideIndex(side)] = double.NaN;
            }
        }
    }

    private double ShelfScaleFor(OutputView view, CanvasSide side, CanvasSetting settings, bool animate, bool pin = true)
    {
        var canvas = view.Canvas;
        var index = SideIndex(side);
        var target = !double.IsNaN(canvas.ShelfOverride[index]) ? canvas.ShelfOverride[index] : settings.ShelfScaleValues.For(side);
        var motion = canvas.ShelfMotion[index];
        if (canvas.ShelfTarget[index] != target)
        {
            var from = motion.IsRunning ? motion.Current : canvas.ShelfTarget[index];
            if (animate && !double.IsNaN(from))
            {
                if (pin)
                {
                    PinShelfWindows(view, side);
                }

                motion.Begin(from, target, CanvasAnimationNanos(view));
                if (motion.IsRunning)
                {
                    ScheduleEffectRepaint();
                }
            }
            else
            {
                motion.Cancel();
            }

            canvas.ShelfTarget[index] = target;
        }

        return motion.IsRunning ? motion.Current : target;
    }

    private void ClearShelves(OutputView view)
    {
        var canvas = view.Canvas;
        for (var i = 0; i < 4; i++)
        {
            canvas.ShelfMotion[i].Cancel();
            canvas.ShelfTarget[i] = double.NaN;
        }

        foreach (var (window, state) in _canvasStates)
        {
            if (state.ShelfPinned && ViewOfWindow(window) == view)
            {
                state.ShelfPinned = false;
            }
        }
    }

    private void ApplyCanvasTerrace(IGrabTarget window, SceneTree tree, CanvasView canvas, CanvasWindowState? state)
    {
        if (state is null)
        {
            if (canvas.IsIdentity)
            {
                return;
            }

            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var transform = state.Transform;
        Bind(transform, canvas);
        transform.SceneX = tree.X;
        transform.SceneY = tree.Y;
        transform.CellSize = canvas.Settings.MeshCellSize;
        var box = CanvasBoxOf(window);
        var (anchorX, anchorY) = state.Anchor is { } anchor
            ? (window.X + anchor.X, window.Y + anchor.Y)
            : (box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        var region = ClassifyTerrace(window, canvas, state, box, anchorX, anchorY);
        state.Region = region;
        var node = state.Node;
        if (region == CanvasRegion.Flat)
        {
            var rest = transform.ViewScale == 1.0 ? RenderTransform.Identity : transform.ViewPlacement();
            if (!rest.IsIdentity && !IsTerraceSettling(state))
            {
                rest = SnapPlacement(window, rest);
            }

            var flat = state.Blend.IsRunning
                ? CanvasScale.Blend(state.BlendFrom, rest, state.Blend.Current)
                : rest;
            if (node is not null && !node.IsDestroyed)
            {
                AdoptStrays(tree, node);
                node.Deformer = null;
                SetCanvasMatrix(node, tree, flat);
            }
            else if (!flat.IsIdentity)
            {
                node = EnsureCanvasNode(tree, state);
                AdoptStrays(tree, node);
                SetCanvasMatrix(node, tree, flat);
            }

            state.Placement = flat;
            state.Scale = flat.IsIdentity ? 1.0 : flat.M11;
            state.Deformed = !flat.IsIdentity;
            state.AppliedGeneration = canvas.Generation;
            state.AppliedSceneX = tree.X;
            state.AppliedSceneY = tree.Y;
            if (_overrideRedirects.Count > 0)
            {
                SyncOverrideRedirects(window, flat);
            }

            SyncScaleOffer(window, state, canvas);
            return;
        }

        node = EnsureCanvasNode(tree, state);
        AdoptStrays(tree, node);
        var flatSlope = canvas.Settings.OnSlopeValue == SlopeWindow.Flat;
        if (region == CanvasRegion.Shelf || (region == CanvasRegion.Corner && canvas.Map.Separable) || flatSlope)
        {
            RenderTransform target;
            if (state.Resizing)
            {
                var start = state.ResizeStart;
                var fixedX = state.ResizeFixedLeft ? start.Right : start.X;
                var fixedY = state.ResizeFixedTop ? start.Bottom : start.Y;
                target = CanvasScale.About(state.ResizeScale, fixedX, fixedY, state.ResizeScreenX, state.ResizeScreenY);
            }
            else
            {
                target = region == CanvasRegion.Shelf || (region == CanvasRegion.Corner && canvas.Map.Separable)
                    ? transform.ShelfPlacement(box.Translated(-tree.X, -tree.Y))
                    : transform.FlatPlacement();
                if (!IsTerraceSettling(state))
                {
                    target = SnapPlacement(window, target);
                }
            }

            var placement = state.Blend.IsRunning ? CanvasScale.Blend(state.BlendFrom, target, state.Blend.Current) : target;
            node.Deformer = null;
            SetCanvasMatrix(node, tree, placement);
            state.Placement = placement;
            state.Scale = placement.M11;
            state.Deformed = true;
            state.AppliedGeneration = canvas.Generation;
            state.AppliedSceneX = tree.X;
            state.AppliedSceneY = tree.Y;
            SyncOverrideRedirects(window, placement);
            SyncScaleOffer(window, state, canvas);
            return;
        }

        node.Matrix = RenderTransform.Identity;
        state.Placement = RenderTransform.Identity;
        state.Scale = 1.0;
        if (_overrideRedirects.Count > 0)
        {
            SyncOverrideRedirects(window, RenderTransform.Identity);
        }

        if (ReferenceEquals(node.Deformer, transform))
        {
            if (state.AppliedGeneration != canvas.Generation || state.AppliedSceneX != tree.X ||
                state.AppliedSceneY != tree.Y || state.AppliedFit != state.Fit ||
                state.AppliedAnchorX != anchorX || state.AppliedAnchorY != anchorY)
            {
                node.NotifyDeformed();
            }
        }
        else
        {
            node.Deformer = transform;
        }

        state.Deformed = true;
        state.AppliedGeneration = canvas.Generation;
        state.AppliedSceneX = tree.X;
        state.AppliedSceneY = tree.Y;
        state.AppliedFit = state.Fit;
        state.AppliedAnchorX = anchorX;
        state.AppliedAnchorY = anchorY;
        SyncScaleOffer(window, state, canvas);
    }

    private CanvasRegion ClassifyTerrace(
        IGrabTarget window, CanvasView canvas, CanvasWindowState state, in Box box, double anchorX, double anchorY)
    {
        var transform = state.Transform;
        if (_canvasSuspended || canvas.IsIdentity)
        {
            transform.PreScale = 1.0;
            state.Fit = 1.0;
            return CanvasRegion.Flat;
        }

        var shelfMin = canvas.Settings.ShelfMinScaleValue;
        var fit = canvas.Scale.SolveFit(canvas.Map, box, shelfMin, anchorX, anchorY);
        if (state.Dragging && state.DragScaleCorrection != 0)
        {
            var travel = Math.Sqrt(
                ((state.DragCursorX - state.DragStartX) * (state.DragCursorX - state.DragStartX)) +
                ((state.DragCursorY - state.DragStartY) * (state.DragCursorY - state.DragStartY)));
            var settle = Math.Max(0.0, 1.0 - (travel / GrabSettlePixels));
            fit = Math.Clamp(fit + (state.DragScaleCorrection * settle), canvas.Scale.FitFor(canvas.Map, box, shelfMin), 1.0);
        }

        transform.PreScale = fit;
        transform.PreAnchorX = anchorX;
        transform.PreAnchorY = anchorY;
        if (canvas.Map.Separable)
        {
            var (localX, localY) = canvas.Map.LocalScale(anchorX, anchorY);
            var even = Math.Min(localX, localY);
            transform.PreStretchX = even / localX;
            transform.PreStretchY = even / localY;
        }

        state.Fit = fit;
        var local = box.Translated(-window.EffectTree!.X, -window.EffectTree.Y);
        if (state.Resizing)
        {
            return CanvasRegion.Shelf;
        }

        if (transform.IsFlatFor(local))
        {
            return CanvasRegion.Flat;
        }

        if (!transform.IsPastFeet(local))
        {
            return CanvasRegion.Slope;
        }

        var (side, end) = ZonesHolding(canvas, box);
        return side is not null && end is not null && !state.Resizing ? CanvasRegion.Corner : CanvasRegion.Shelf;
    }

    private static bool IsTerraceSettling(CanvasWindowState state) =>
        state.Dragging || state.Resizing || state.MotionX.IsRunning || state.MotionY.IsRunning || state.ShelfPinned;

    private SceneTransform EnsureCanvasNode(SceneTree tree, CanvasWindowState state)
    {
        if (state.Node is { IsDestroyed: false } node)
        {
            return node;
        }

        var stack = _effects.StackFor(tree);
        node = stack.Get(CanvasTransformName) ?? stack.Add(CanvasZ, CanvasTransformName);
        state.Node = node;
        return node;
    }

    private static void SetCanvasMatrix(SceneTransform node, SceneTree tree, in RenderTransform placement) =>
        node.Matrix = placement.IsIdentity
            ? RenderTransform.Identity
            : RenderTransform.Multiply(
                RenderTransform.Translation(-tree.X, -tree.Y),
                RenderTransform.Multiply(placement, RenderTransform.Translation(tree.X, tree.Y)));

    private void BeginTerraceGrab(IGrabTarget window, OutputView view, double x, double y)
    {
        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var canvas = view.Canvas;
        var drawn = state.Fit;
        state.Blend.Cancel();
        state.DragGrabX = _grabX;
        state.DragGrabY = _grabY;
        state.DragCursorX = x;
        state.DragCursorY = y;
        state.DragStartX = x;
        state.DragStartY = y;
        var (grabX, grabY) = canvas.ToCanvasPoint(x, y);
        state.DragFieldX = grabX;
        state.DragFieldY = grabY;
        var dx = (int)Math.Round(grabX - _grabX) - window.X;
        var dy = (int)Math.Round(grabY - _grabY) - window.Y;
        var moved = CanvasBoxOf(window).Translated(dx, dy);
        var hand = canvas.Scale.SolveFit(
            canvas.Map, moved, canvas.Settings.ShelfMinScaleValue, window.X + dx + _grabX, window.Y + dy + _grabY);
        state.DragScaleCorrection = drawn - hand;
        state.Anchor = (_grabX, _grabY);
        state.ShelfPinned = false;
        state.Dragging = true;
        _canvasScaleDrag = true;
        if (dx != 0 || dy != 0)
        {
            window.MoveTo(window.X + dx, window.Y + dy);
        }
        else
        {
            ApplyCanvas(window);
        }
    }

    private void EndTerraceGrab(IGrabTarget window, OutputView view, CanvasWindowState state, in RenderTransform from, bool dropped)
    {
        if (!from.IsIdentity)
        {
            state.BlendFrom = from;
            state.Blend.Cancel();
            state.Blend.Begin(0.0, 1.0, dropped ? Math.Min(DropSettleNanos, CanvasAnimationNanos(view)) : CanvasAnimationNanos(view));
            if (state.Blend.IsRunning && !_canvasMotions.Contains(window))
            {
                _canvasMotions.Add(window);
                ScheduleEffectRepaint();
            }
        }

        ApplyCanvas(window);
    }

    private (int X, int Y) TerraceParkTargets(CanvasView canvas, IGrabTarget window, in Box box, CanvasWarp? side, CanvasWarp? end)
    {
        var offsetX = window.X - box.X;
        var offsetY = window.Y - box.Y;
        var floor = canvas.Settings.ShelfMinScaleValue;
        if (_canvasStates.TryGetValue(window, out var state))
        {
            state.Anchor = null;
        }

        if (side is not null && end is not null)
        {
            var (x, y) = canvas.Scale.TerraceCornerParkTarget(canvas.Map, side, end, box, floor);
            return (x + offsetX, y + offsetY);
        }

        if (side is not null)
        {
            return (canvas.Scale.TerraceParkTarget(canvas.Map, side, box, floor) + offsetX, window.Y);
        }

        return end is not null
            ? (window.X, canvas.Scale.TerraceParkTarget(canvas.Map, end, box, floor) + offsetY)
            : (window.X, window.Y);
    }

    private void BlendTerraceWindow(IGrabTarget window, CanvasView canvas, CanvasWindowState state, long nanos)
    {
        if (canvas.Step is not null)
        {
            state.BlendFrom = state.Placement;
        }
        else if (_canvasSuspended)
        {
            if (state.Placement.IsIdentity)
            {
                return;
            }

            state.BlendFrom = state.Placement;
        }
        else
        {
            var box = CanvasBoxOf(window);
            var (anchorX, anchorY) = state.Anchor is { } anchor
                ? (window.X + anchor.X, window.Y + anchor.Y)
                : (box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            Bind(state.Transform, canvas);
            state.Transform.SceneX = window.EffectTree!.X;
            state.Transform.SceneY = window.EffectTree.Y;
            var blended = ClassifyTerrace(window, canvas, state, box, anchorX, anchorY);
            if (blended == CanvasRegion.Flat ||
                (blended != CanvasRegion.Shelf && canvas.Settings.OnSlopeValue != SlopeWindow.Flat &&
                    !(blended == CanvasRegion.Corner && canvas.Map.Separable)))
            {
                return;
            }

            state.BlendFrom = state.Placement;
        }

        state.Blend.Cancel();
        state.Blend.Begin(0.0, 1.0, nanos);
        if (state.Blend.IsRunning && !_canvasMotions.Contains(window))
        {
            _canvasMotions.Add(window);
        }
    }

    private string TerraceWhere(IGrabTarget window)
    {
        if (!_canvasStates.TryGetValue(window, out var state))
        {
            return " region=flat k=1.000 fit=1.000";
        }

        var region = state.Region switch
        {
            CanvasRegion.Slope => "slope",
            CanvasRegion.Shelf => "shelf",
            CanvasRegion.Corner => "corner",
            _ => "flat",
        };
        return string.Create(CultureInfo.InvariantCulture, $" region={region} k={state.Scale:F3} fit={state.Fit:F3}");
    }

    private void PinShelfWindows(OutputView view, CanvasSide side)
    {
        var canvas = view.Canvas;
        var horizontal = (side & CanvasSide.Horizontal) != 0;
        foreach (var (window, state) in _canvasStates)
        {
            if (state.Dragging || state.Resizing || state.MotionX.IsRunning || state.MotionY.IsRunning ||
                state.Region is not (CanvasRegion.Shelf or CanvasRegion.Corner) ||
                (state.Region == CanvasRegion.Shelf && state.Placement.IsIdentity) ||
                window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) != view)
            {
                continue;
            }

            var box = CanvasBoxOf(window);
            var (holdingSide, holdingEnd) = ZonesHolding(canvas, box);
            var placement = state.Placement;
            if (holdingSide is not null && holdingEnd is not null)
            {
                if (!ReferenceEquals(holdingSide, WarpOf(canvas, side)) && !ReferenceEquals(holdingEnd, WarpOf(canvas, side)))
                {
                    continue;
                }

                state.ShelfPinned = true;
                state.PinSide = (holdingSide.Direction < 0 ? CanvasSide.Left : CanvasSide.Right) |
                    (holdingEnd.Direction < 0 ? CanvasSide.Top : CanvasSide.Bottom);
                var (pinX, pinY) = state.Transform.ToScreenPoint(
                    holdingSide.Direction < 0 ? box.X : box.Right, holdingEnd.Direction < 0 ? box.Y : box.Bottom);
                (state.PinOuter, state.PinCenter) = canvas.Map.Unzoom(pinX, pinY);
                continue;
            }

            var holding = holdingSide ?? holdingEnd;
            if (holding is null || !ReferenceEquals(holding, WarpOf(canvas, side)))
            {
                continue;
            }

            double outer;
            double center;
            if (horizontal)
            {
                outer = (placement.M11 * (side == CanvasSide.Left ? box.X : box.Right)) + placement.M13;
                center = (placement.M22 * (box.Y + (box.Height / 2.0))) + placement.M23;
            }
            else
            {
                outer = (placement.M22 * (side == CanvasSide.Top ? box.Y : box.Bottom)) + placement.M23;
                center = (placement.M11 * (box.X + (box.Width / 2.0))) + placement.M13;
            }

            var (warpOuter, warpCenter) = horizontal ? canvas.Map.Unzoom(outer, center) : canvas.Map.Unzoom(center, outer);
            state.ShelfPinned = true;
            state.PinSide = side;
            state.PinOuter = horizontal ? warpOuter : warpCenter;
            state.PinCenter = horizontal ? warpCenter : warpOuter;
        }
    }

    private void ResolveShelfPins(OutputView view)
    {
        var canvas = view.Canvas;
        var floor = canvas.Settings.ShelfMinScaleValue;
        foreach (var (window, state) in _canvasStates)
        {
            if (!state.ShelfPinned || window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) != view)
            {
                continue;
            }

            var box = CanvasBoxOf(window);
            if ((state.PinSide & CanvasSide.Horizontal) != 0 && (state.PinSide & CanvasSide.Vertical) != 0)
            {
                var cornerSide = WarpOf(canvas, state.PinSide & CanvasSide.Horizontal);
                var cornerEnd = WarpOf(canvas, state.PinSide & CanvasSide.Vertical);
                if (cornerSide.IsIdentity || cornerEnd.IsIdentity || !cornerSide.Terrace || !cornerEnd.Terrace)
                {
                    state.ShelfPinned = false;
                    continue;
                }

                var (cornerX, cornerY) = canvas.Scale.TerraceCornerTarget(
                    canvas.Map, cornerSide, cornerEnd, box, floor, state.PinOuter, state.PinCenter);
                state.Anchor = null;
                var movedX = cornerX + (window.X - box.X);
                var movedY = cornerY + (window.Y - box.Y);
                if (movedX != window.X || movedY != window.Y)
                {
                    window.MoveTo(movedX, movedY);
                }

                continue;
            }

            var warp = WarpOf(canvas, state.PinSide);
            if (warp.IsIdentity || !warp.Terrace)
            {
                state.ShelfPinned = false;
                continue;
            }

            var horizontal = (state.PinSide & CanvasSide.Horizontal) != 0;
            var size = horizontal ? box.Width : box.Height;
            var fit = canvas.Scale.FitAtDepth(
                canvas.Map, box, floor, horizontal ? state.PinOuter : 0.0, horizontal ? 0.0 : state.PinOuter);
            var outer = warp.ToCanvas(state.PinOuter);
            var origin = outer - (warp.Direction * size * fit / 2.0) - (size / 2.0);
            var across = warp.ToCanvasY(state.PinOuter - (warp.Direction * 0.5), state.PinCenter);
            if (canvas.Map.Separable)
            {
                var (inverseX, inverseY) = canvas.Map.FromWarpPoint(
                    horizontal ? state.PinOuter - (warp.Direction * 0.5) : state.PinCenter,
                    horizontal ? state.PinCenter : state.PinOuter - (warp.Direction * 0.5));
                across = horizontal ? inverseY : inverseX;
            }

            state.Anchor = null;
            var x = window.X;
            var y = window.Y;
            if (horizontal)
            {
                x = (int)Math.Round(origin) + (window.X - box.X);
                y = (int)Math.Round(across - (box.Height / 2.0)) + (window.Y - box.Y);
            }
            else
            {
                y = (int)Math.Round(origin) + (window.Y - box.Y);
                x = (int)Math.Round(across - (box.Width / 2.0)) + (window.X - box.X);
            }

            if (x != window.X || y != window.Y)
            {
                window.MoveTo(x, y);
            }
        }
    }

    private bool StepShelfMotions(in FrameTick tick)
    {
        var running = false;
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is not OutputPolicy || !view.Canvas.ShelfAnimating)
            {
                continue;
            }

            var canvas = view.Canvas;
            for (var side = 0; side < 4; side++)
            {
                if (canvas.ShelfMotion[side].IsRunning)
                {
                    _ = canvas.ShelfMotion[side].Step(tick, out _);
                }
            }

            LayoutCanvas(view, animating: true);
            if (_mode == DragMode.Move && _grabWindow is { } held && _canvasStates.TryGetValue(held, out var heldState) &&
                heldState.Dragging && ViewOfWindow(held) == view)
            {
                _ = DragCanvasScaled(held, _cursorX, _cursorY);
            }

            if (canvas.ShelfAnimating)
            {
                running = true;
                continue;
            }

            foreach (var (window, state) in _canvasStates)
            {
                if (state.ShelfPinned && ViewOfWindow(window) == view)
                {
                    state.ShelfPinned = false;
                    ApplyCanvas(window);
                }
            }
        }

        return running;
    }

    private void ShelfTargets(out OutputView? view, out CanvasSide sides)
    {
        sides = CanvasSide.None;
        view = null;
        if (OverviewTargetView() is { } stepView && Stepped(stepView))
        {
            view = stepView;
            sides = OverviewOf(stepView).Sides;
            return;
        }

        if (FocusedGrabTarget() is { } focused && ViewOfWindow(focused) is { Tag: OutputPolicy } focusedView)
        {
            view = focusedView;
            var canvas = focusedView.Canvas;
            var box = CanvasBoxOf(focused);
            if (canvas.Left.ContainsCanvas(box.X))
            {
                sides |= CanvasSide.Left;
            }
            else if (canvas.Right.ContainsCanvas(box.Right))
            {
                sides |= CanvasSide.Right;
            }

            if (canvas.Top.ContainsCanvas(box.Y))
            {
                sides |= CanvasSide.Top;
            }
            else if (canvas.Bottom.ContainsCanvas(box.Bottom))
            {
                sides |= CanvasSide.Bottom;
            }

            if (sides != CanvasSide.None)
            {
                return;
            }
        }

        if (ViewAt(_cursorX, _cursorY) is { Tag: OutputPolicy } pointed)
        {
            var canvas = pointed.Canvas;
            var (warpX, warpY) = canvas.Map.Unzoom(_cursorX, _cursorY);
            if (canvas.Left.ContainsScreen(warpX))
            {
                sides |= CanvasSide.Left;
            }
            else if (canvas.Right.ContainsScreen(warpX))
            {
                sides |= CanvasSide.Right;
            }

            if (canvas.Top.ContainsScreen(warpY))
            {
                sides |= CanvasSide.Top;
            }
            else if (canvas.Bottom.ContainsScreen(warpY))
            {
                sides |= CanvasSide.Bottom;
            }

            if (sides != CanvasSide.None)
            {
                view = pointed;
                return;
            }

            view ??= pointed;
        }

        view ??= Views.Count > 0 && Views[0].Tag is OutputPolicy ? Views[0] : null;
        if (view is null)
        {
            return;
        }

        foreach (var side in CanvasSides.Each)
        {
            if (!WarpOf(view.Canvas, side).IsIdentity)
            {
                sides |= side;
            }
        }
    }

    internal void AdjustShelf(CanvasSide? only, double? scale, int step, bool reset)
    {
        ShelfTargets(out var view, out var sides);
        if (only is { } side)
        {
            sides = side;
        }

        if (view is null)
        {
            _report.Line("SHELF refused: no output");
            return;
        }

        var canvas = view.Canvas;
        var index = ViewIndex(view);
        if (!canvas.Terraces)
        {
            _report.Line($"SHELF view={index} refused: window={CanvasSetting.NameOf(canvas.Mode)}");
            return;
        }

        if (sides == CanvasSide.None)
        {
            _report.Line($"SHELF view={index} refused: no active side");
            return;
        }

        foreach (var each in CanvasSides.Each)
        {
            if ((sides & each) == CanvasSide.None)
            {
                continue;
            }

            var i = SideIndex(each);
            var from = ShelfScaleInForce(canvas, each);
            var configured = canvas.Settings.ShelfScaleValues.For(each);
            double next;
            if (reset)
            {
                next = configured;
                canvas.ShelfOverride[i] = double.NaN;
            }
            else
            {
                next = scale ?? (from + (step * canvas.Settings.ShelfStepValue));
                next = Math.Round(Math.Clamp(next, CanvasSetting.MinShelfScale, CanvasSetting.MaxShelfScale), 6);
                canvas.ShelfOverride[i] = next;
            }

            var held = !reset && next == from ? " held" : string.Empty;
            _report.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"SHELF view={index} side={ZoneName(each)} scale={next:F2} from={from:F2}{held}"));
        }

        LayoutCanvas(view);
    }

    internal void ReportShelves()
    {
        ShelfTargets(out var view, out _);
        if (view is null)
        {
            _report.Line("SHELF refused: no output");
            return;
        }

        _report.Line($"SHELF view={ViewIndex(view)} window={CanvasSetting.NameOf(view.Canvas.Mode)} scales={ShelfScaleNames(view.Canvas)}");
    }

    internal void WriteShelves(System.Text.Json.Utf8JsonWriter json)
    {
        ShelfTargets(out var view, out _);
        json.WriteStartObject();
        if (view is not null)
        {
            var canvas = view.Canvas;
            json.WriteNumber("view", ViewIndex(view));
            json.WriteString("window", CanvasSetting.NameOf(canvas.Mode));
            json.WriteNumber("left", ShelfScaleInForce(canvas, CanvasSide.Left));
            json.WriteNumber("right", ShelfScaleInForce(canvas, CanvasSide.Right));
            json.WriteNumber("top", ShelfScaleInForce(canvas, CanvasSide.Top));
            json.WriteNumber("bottom", ShelfScaleInForce(canvas, CanvasSide.Bottom));
        }

        json.WriteEndObject();
    }

    internal CanvasWindowMode? SetCanvasMode(CanvasWindowMode? mode)
    {
        var view = FocusedGrabTarget() is { } focused && ViewOfWindow(focused) is { Tag: OutputPolicy } focusedView
            ? focusedView
            : ViewAtCursor() is { Tag: OutputPolicy } pointed ? pointed : null;
        if (view is null)
        {
            _report.Line("CANVASMODE refused: no output");
            return null;
        }

        var canvas = view.Canvas;
        if (canvas.Overview)
        {
            _report.Line("CANVASMODE refused: overview");
            return null;
        }

        var next = mode ?? canvas.Mode switch
        {
            CanvasWindowMode.Warp => CanvasWindowMode.Scale,
            CanvasWindowMode.Scale => CanvasWindowMode.Terrace,
            _ => CanvasWindowMode.Warp,
        };
        canvas.ModeOverride = next;
        LayoutCanvas(view);
        _report.Line($"CANVASMODE view={ViewIndex(view)} window={CanvasSetting.NameOf(canvas.Mode)}");
        return canvas.Mode;
    }
}
