using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const string CanvasTransformName = "canvas";

    private const int CanvasZ = TransformStack.ZOrder.Backdrop + 1000;

    private const uint XResourceIdMask = 0x001FFFFF;

    private const double GrabSettlePixels = 96.0;

    private const long DropSettleNanos = 150_000_000;

    private readonly Dictionary<IGrabTarget, CanvasWindowState> _canvasStates = [];
    private readonly List<IGrabTarget> _canvasMotions = [];
    private readonly List<IGrabTarget> _canvasMotionsDone = [];
    private bool _canvasSuspended;
    private bool? _canvasOverride;
    private bool _canvasClosing;
    private bool _canvasGridDragging;
    private readonly List<OverrideRedirectFrame> _overrideRedirects = [];
    private bool _canvasScaleDrag;

    private bool CanvasWanted(OutputView view) => _canvasOverride ?? view.Canvas.Settings.Enabled;

    private void LayoutCanvasAll()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            LayoutCanvas(view);
        }
    }

    private void LayoutCanvas(OutputView view, bool animating = false)
    {
        if (view.Tag is not OutputPolicy || !_layout.Contains(view.Output))
        {
            return;
        }

        var canvas = view.Canvas;
        var previous = canvas.Settings;
        var settings = animating ? previous : _config.CanvasFor(view.Output.Name, _log);
        if (!animating)
        {
            ForgetStaleOverrides(canvas, previous, settings);
        }

        canvas.Settings = settings;
        var mode = canvas.ModeOverride ?? settings.WindowMode;
        var terrace = mode == CanvasWindowMode.Terrace;
        var box = _layout.BoxOf(view.Output);
        var enabled = CanvasWanted(view) && !box.IsEmpty;
        canvas.Enabled = enabled;
        var fullscreen = AnyFullscreenOn(view);
        var usable = UsableBox(view, box);
        var sides = settings.SideSet;
        var (terraceZone, terraceShelf) = settings.TerraceFractions;
        var zoneFraction = terrace ? terraceZone : settings.ZoneFractionFor(mode);
        var zone = (int)Math.Round(zoneFraction * box.Width);
        var extension = (int)Math.Round(settings.ExtensionFraction * box.Width);
        var zoneY = (int)Math.Round(zoneFraction * box.Height);
        var extensionY = (int)Math.Round(settings.ExtensionFraction * box.Height);
        var shelf = terrace ? (int)Math.Round(terraceShelf * box.Width) : 0;
        var shelfY = terrace ? (int)Math.Round(terraceShelf * box.Height) : 0;
        var open = enabled && !fullscreen;
        var horizontal = open && zone > 0 && (terrace || extension > 0);
        var vertical = open && zoneY > 0 && (terrace || extensionY > 0);
        var leftActive = horizontal && sides.HasFlag(CanvasSide.Left) && !HasNeighbour(view.Output, box, CanvasSide.Left);
        var rightActive = horizontal && sides.HasFlag(CanvasSide.Right) && !HasNeighbour(view.Output, box, CanvasSide.Right);
        var topActive = vertical && sides.HasFlag(CanvasSide.Top) && !HasNeighbour(view.Output, box, CanvasSide.Top);
        var bottomActive = vertical && sides.HasFlag(CanvasSide.Bottom) && !HasNeighbour(view.Output, box, CanvasSide.Bottom);
        var centerY = usable.Y + (usable.Height / 2.0);
        var centerX = usable.X + (usable.Width / 2.0);
        bool changed;
        if (terrace)
        {
            var wasTerrace = canvas.Terraces;
            var left = ShelfScaleFor(view, CanvasSide.Left, settings, wasTerrace && leftActive);
            var right = ShelfScaleFor(view, CanvasSide.Right, settings, wasTerrace && rightActive);
            var top = ShelfScaleFor(view, CanvasSide.Top, settings, wasTerrace && topActive);
            var bottom = ShelfScaleFor(view, CanvasSide.Bottom, settings, wasTerrace && bottomActive);
            changed = canvas.Left.LayoutTerrace(
                usable.X + shelf + zone, -1, leftActive ? zone : 0, shelf, left, CanvasWarp.TerraceExponent,
                settings.SlopeValue, centerY);
            changed |= canvas.Right.LayoutTerrace(
                usable.Right - shelf - zone, 1, rightActive ? zone : 0, shelf, right, CanvasWarp.TerraceExponent,
                settings.SlopeValue, centerY);
            changed |= canvas.Top.LayoutTerrace(
                usable.Y + shelfY + zoneY, -1, topActive ? zoneY : 0, shelfY, top, CanvasWarp.TerraceExponent,
                settings.SlopeValue, centerX);
            changed |= canvas.Bottom.LayoutTerrace(
                usable.Bottom - shelfY - zoneY, 1, bottomActive ? zoneY : 0, shelfY, bottom, CanvasWarp.TerraceExponent,
                settings.SlopeValue, centerX);
        }
        else
        {
            ClearShelves(view);
            changed = canvas.Left.Layout(
                usable.X + zone, -1, leftActive ? zone : 0, leftActive ? extension : 0, settings.EdgeScaleValue,
                settings.SlopeValue, centerY);
            changed |= canvas.Right.Layout(
                usable.Right - zone, 1, rightActive ? zone : 0, rightActive ? extension : 0, settings.EdgeScaleValue,
                settings.SlopeValue, centerY);
            changed |= canvas.Top.Layout(
                usable.Y + zoneY, -1, topActive ? zoneY : 0, topActive ? extensionY : 0, settings.EdgeScaleValue,
                settings.SlopeValue, centerX);
            changed |= canvas.Bottom.Layout(
                usable.Bottom - zoneY, 1, bottomActive ? zoneY : 0, bottomActive ? extensionY : 0, settings.EdgeScaleValue,
                settings.SlopeValue, centerX);
        }

        var separable = terrace && settings.ShapeValue == ShelfShape.Flat;
        if (canvas.Map.Separable != separable)
        {
            canvas.Map.Separable = separable;
            changed = true;
        }

        var modeChanged = canvas.Mode != mode;
        if (modeChanged || canvas.Scale.MinScale != Math.Clamp(settings.MinScaleValue, 0.05, 1.0) ||
            canvas.Scale.Reach != Math.Clamp(settings.ScaleReachValue, 1.0, 16.0))
        {
            canvas.Mode = mode;
            canvas.Scale.MinScale = settings.MinScaleValue;
            canvas.Scale.Reach = settings.ScaleReachValue;
            changed = true;
        }

        if (canvas.Map.CornerRadius != settings.CornerRadiusValue || canvas.Map.CornerTaper != settings.CornerTaperValue)
        {
            canvas.Map.CornerRadius = settings.CornerRadiusValue;
            canvas.Map.CornerTaper = settings.CornerTaperValue;
            changed = true;
        }

        changed |= LayoutCanvasGrid(view, box, enabled && settings.GridMode != CanvasGridMode.Never);
        if (!changed)
        {
            return;
        }

        canvas.Generation++;
        if (animating)
        {
            ResolveShelfPins(view);
            ApplyCanvasToView(view);
            canvas.Grid?.NotifyMeshChanged();
            view.Scheduler?.ScheduleRepaint();
            return;
        }

        var active = !canvas.Left.IsIdentity ? canvas.Left : !canvas.Right.IsIdentity ? canvas.Right
            : !canvas.Top.IsIdentity ? canvas.Top : !canvas.Bottom.IsIdentity ? canvas.Bottom : null;
        _report.Line(
            $"CANVAS view={ViewIndex(view)} left={canvas.Left.ZoneWidth} right={canvas.Right.ZoneWidth}"
            + $" top={canvas.Top.ZoneWidth} bottom={canvas.Bottom.ZoneWidth} corner={(canvas.Map.CornerTaper ? "taper" : "radius")}:{canvas.Map.CornerRadius:F2}"
            + $" extension={active?.Extension ?? 0} edge_scale={(active?.EdgeScale ?? settings.EdgeScaleValue):F3}"
            + $" exponent={(active?.Exponent ?? 0):F2} slope={(active?.Slope ?? 0):F2}"
            + $" window={CanvasSetting.NameOf(mode)} min_scale={canvas.Scale.MinScale:F2} reach={canvas.Scale.Reach:F2}"
            + $" shelf={canvas.Right.ShelfWidth} shelf_scale={ShelfScaleNames(canvas)} shelf_min={settings.ShelfMinScaleValue:F2}"
            + $" shelf_shape={(canvas.Map.Separable ? "flat" : "square")} slope_window={settings.OnSlopeName}");
        KeepParkedInside(view, modeChanged);
        ApplyCanvasToView(view);
        canvas.Grid?.NotifyMeshChanged();
        view.Scheduler?.ScheduleRepaint();
    }

    private bool HasNeighbour(IOutput output, in Box box, CanvasSide side)
    {
        foreach (var (other, _) in _layout.Outputs)
        {
            if (ReferenceEquals(other, output))
            {
                continue;
            }

            var otherBox = _layout.BoxOf(other);
            if (otherBox.IsEmpty)
            {
                continue;
            }

            var touches = side switch
            {
                CanvasSide.Left => otherBox.Right == box.X,
                CanvasSide.Right => otherBox.X == box.Right,
                CanvasSide.Top => otherBox.Bottom == box.Y,
                _ => otherBox.Y == box.Bottom,
            };
            var overlaps = (side & CanvasSide.Horizontal) != 0
                ? otherBox.Bottom > box.Y && otherBox.Y < box.Bottom
                : otherBox.Right > box.X && otherBox.X < box.Right;
            if (touches && overlaps)
            {
                return true;
            }
        }

        return false;
    }

    private int ViewIndex(OutputView view)
    {
        for (var i = 0; i < Views.Count; i++)
        {
            if (ReferenceEquals(Views[i], view))
            {
                return i;
            }
        }

        return -1;
    }

    private bool LayoutCanvasGrid(OutputView view, in Box box, bool wanted)
    {
        var canvas = view.Canvas;
        if (!wanted)
        {
            if (canvas.Grid is null)
            {
                return false;
            }

            canvas.Grid.Destroy();
            canvas.Grid = null;
            canvas.GridSource = null;
            return true;
        }

        var settings = canvas.Settings;
        var source = canvas.GridSource ??= new CanvasGridSource
        {
            Left = canvas.Left,
            Right = canvas.Right,
            Top = canvas.Top,
            Bottom = canvas.Bottom,
        };
        var color = settings.GridRenderColor;
        var alpha = GridAlphaFor(settings);
        var spacing = canvas.Terraces ? TerraceGridSpacing : 0.0;
        var changed = source.CellSize != settings.GridCellSize || source.Color != color || source.Alpha != alpha ||
            source.CornerRadius != canvas.Map.CornerRadius || source.CornerTaper != canvas.Map.CornerTaper ||
            source.MinLineSpacing != spacing || source.Separable != canvas.Map.Separable;
        source.MinLineSpacing = spacing;
        source.Separable = canvas.Map.Separable;
        source.CornerRadius = canvas.Map.CornerRadius;
        source.CornerTaper = canvas.Map.CornerTaper;
        source.CellSize = settings.GridCellSize;
        source.Color = color;
        source.Alpha = alpha;
        if (canvas.Grid is null)
        {
            canvas.Grid = new SceneMesh(_layers.Background) { Source = source, Bounds = box };
            return true;
        }

        changed |= canvas.Grid.Bounds != box;
        canvas.Grid.Bounds = box;
        return changed;
    }

    private float GridAlphaFor(CanvasSetting settings) => settings.GridMode switch
    {
        CanvasGridMode.Always => 1f,
        CanvasGridMode.Drag => _canvasGridDragging ? 1f : 0f,
        _ => 0f,
    };

    private void SetCanvasGridDragging(bool dragging)
    {
        if (_canvasGridDragging == dragging)
        {
            return;
        }

        _canvasGridDragging = dragging;
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is not OutputPolicy || view.Canvas is not { GridSource: { } source, Grid: { } grid })
            {
                continue;
            }

            var alpha = GridAlphaFor(view.Canvas.Settings);
            if (source.Alpha != alpha)
            {
                source.Alpha = alpha;
                grid.NotifyMeshChanged();
            }
        }
    }

    private bool AnyFullscreenOn(OutputView view)
    {
        foreach (var window in _windows)
        {
            if (!window.Minimized && window.Tree is { IsDestroyed: false } &&
                window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen) &&
                ViewOfWindow(window) == view)
            {
                return true;
            }
        }

        return false;
    }

    private OutputView? ViewOfWindow(IGrabTarget window)
    {
        var (width, height) = window.GeometrySize;
        var windowRight = window.X + Math.Max(width, 1);
        var windowBottom = window.Y + Math.Max(height, 1);
        OutputView? best = null;
        var bestOverlap = 0L;
        for (var i = 0; i < Views.Count; i++)
        {
            var candidate = Views[i];
            if (candidate.Tag is not OutputPolicy || !_layout.Contains(candidate.Output))
            {
                continue;
            }

            var box = _layout.BoxOf(candidate.Output);
            var canvas = candidate.Canvas;
            var spanLeft = canvas.Left.IsIdentity ? box.X : SpanEdge(canvas.Left);
            var spanRight = canvas.Right.IsIdentity ? box.Right : SpanEdge(canvas.Right);
            var spanTop = canvas.Top.IsIdentity ? box.Y : SpanEdge(canvas.Top);
            var spanBottom = canvas.Bottom.IsIdentity ? box.Bottom : SpanEdge(canvas.Bottom);
            var overlapX = Math.Min(windowRight, spanRight) - Math.Max(window.X, spanLeft);
            var overlapY = Math.Min(windowBottom, spanBottom) - Math.Max(window.Y, spanTop);
            if (overlapX <= 0 || overlapY <= 0)
            {
                continue;
            }

            var overlap = (long)overlapX * overlapY;
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = candidate;
            }
        }

        if (best is not null)
        {
            return best;
        }

        var workspace = window switch
        {
            Window w => w.Workspace,
            XWindow x => x.Workspace,
            _ => null,
        };
        if (workspace is not null)
        {
            for (var i = 0; i < Views.Count; i++)
            {
                if (Views[i].Tag is OutputPolicy policy && policy.Workspaces.Contains(workspace))
                {
                    return Views[i];
                }
            }
        }

        return Views.Count > 0 ? Views[0] : null;
    }

    private void KeepParkedInside(OutputView view, bool modeChanged)
    {
        var canvas = view.Canvas;
        foreach (var (window, state) in _canvasStates)
        {
            if (state.Home is null || state.MotionX.IsRunning || state.MotionY.IsRunning || state.ShelfPinned ||
                window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) != view)
            {
                continue;
            }

            var box = CanvasBoxOf(window);
            if (canvas.Scales || canvas.Terraces || modeChanged)
            {
                var (side, end) = ZonesHolding(canvas, box);
                if (side is null && end is null)
                {
                    continue;
                }

                var (parkX, parkY) = ParkTargets(canvas, window, box, side, end);
                if (parkX != window.X || parkY != window.Y)
                {
                    window.MoveTo(parkX, parkY);
                }

                continue;
            }

            var dx = 0;
            var dy = 0;
            if (!canvas.Left.IsIdentity && box.X < canvas.Left.FarEdge)
            {
                dx = canvas.Left.FarEdge - box.X;
            }
            else if (!canvas.Right.IsIdentity && box.Right > canvas.Right.FarEdge)
            {
                dx = canvas.Right.FarEdge - box.Right;
            }

            if (!canvas.Top.IsIdentity && box.Y < canvas.Top.FarEdge)
            {
                dy = canvas.Top.FarEdge - box.Y;
            }
            else if (!canvas.Bottom.IsIdentity && box.Bottom > canvas.Bottom.FarEdge)
            {
                dy = canvas.Bottom.FarEdge - box.Bottom;
            }

            if (dx != 0 || dy != 0)
            {
                window.MoveTo(window.X + dx, window.Y + dy);
            }
        }
    }

    private void ApplyCanvasToView(OutputView view)
    {
        foreach (var window in _windows)
        {
            if (window.Tree is not null && ViewOfWindow(window) == view)
            {
                ApplyCanvas(window);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Framable && ViewOfWindow(xwindow) == view)
            {
                ApplyCanvas(xwindow);
            }
        }
    }

    private void ApplyCanvasToAll()
    {
        foreach (var window in _windows)
        {
            if (window.Tree is not null)
            {
                ApplyCanvas(window);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Framable)
            {
                ApplyCanvas(xwindow);
            }
        }
    }

    internal void ApplyCanvas(IGrabTarget window)
    {
        if (window.EffectTree is not { IsDestroyed: false } tree || ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return;
        }

        var canvas = view.Canvas;
        var hasState = _canvasStates.TryGetValue(window, out var state);
        if (canvas.IsIdentity && !hasState)
        {
            return;
        }

        if (canvas.Scales)
        {
            ApplyCanvasScale(window, tree, canvas, state);
            return;
        }

        if (canvas.Terraces)
        {
            ApplyCanvasTerrace(window, tree, canvas, state);
            return;
        }

        if (!hasState)
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var node = state!.Node;
        if (node is null || node.IsDestroyed)
        {
            var probe = state.Transform;
            Bind(probe, canvas);
            probe.SceneX = tree.X;
            probe.SceneY = tree.Y;
            if (canvas.IsIdentity || probe.IsIdentityFor(CanvasBoxOf(window).Translated(-window.X, -window.Y)))
            {
                return;
            }

            var stack = _effects.StackFor(tree);
            node = stack.Get(CanvasTransformName) ?? stack.Add(CanvasZ, CanvasTransformName);
            state.Node = node;
        }

        AdoptStrays(tree, node);
        var transform = state.Transform;
        Bind(transform, canvas);
        transform.SceneX = tree.X;
        transform.SceneY = tree.Y;
        transform.CellSize = canvas.Settings.MeshCellSize;
        node.Matrix = RenderTransform.Identity;
        state.Placement = RenderTransform.Identity;
        state.Scale = 1.0;
        if (_overrideRedirects.Count > 0)
        {
            SyncOverrideRedirects(window, RenderTransform.Identity);
        }

        if (state.OfferedScale < 1.0)
        {
            SyncScaleOffer(window, state, canvas);
        }

        var bounds = node.ContentBounds;
        var deformed = !_canvasSuspended && !canvas.IsIdentity && !bounds.IsEmpty && !transform.IsIdentityFor(bounds);
        if (!deformed)
        {
            node.Deformer = null;
            state.Deformed = false;
            state.AppliedGeneration = canvas.Generation;
            state.AppliedSceneX = tree.X;
            return;
        }

        if (ReferenceEquals(node.Deformer, transform))
        {
            if (state.AppliedGeneration != canvas.Generation || state.AppliedSceneX != tree.X ||
                state.AppliedSceneY != tree.Y)
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
    }

    private void ApplyCanvasScale(IGrabTarget window, SceneTree tree, CanvasView canvas, CanvasWindowState? state)
    {
        var placement = canvas.IsIdentity && state?.Blend.IsRunning != true
            ? RenderTransform.Identity
            : ScalePlacementOf(window, canvas, state);
        if (state is null)
        {
            if (placement.IsIdentity)
            {
                return;
            }

            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var node = state.Node;
        if (node is null || node.IsDestroyed)
        {
            if (placement.IsIdentity)
            {
                state.Placement = placement;
                state.Scale = 1.0;
                state.Deformed = false;
                return;
            }

            var stack = _effects.StackFor(tree);
            node = stack.Get(CanvasTransformName) ?? stack.Add(CanvasZ, CanvasTransformName);
            state.Node = node;
        }

        AdoptStrays(tree, node);
        node.Deformer = null;
        node.Matrix = placement.IsIdentity
            ? RenderTransform.Identity
            : RenderTransform.Multiply(
                RenderTransform.Translation(-tree.X, -tree.Y),
                RenderTransform.Multiply(placement, RenderTransform.Translation(tree.X, tree.Y)));
        state.Placement = placement;
        state.Scale = placement.M11;
        state.Deformed = !placement.IsIdentity;
        state.AppliedGeneration = canvas.Generation;
        state.AppliedSceneX = tree.X;
        state.AppliedSceneY = tree.Y;
        SyncOverrideRedirects(window, placement);
        SyncScaleOffer(window, state, canvas);
    }

    private SceneTree AdoptOverrideRedirect(Basin.XWayland.XWaylandWindow xwin)
    {
        var frame = new OverrideRedirectFrame(xwin, new SceneTransform(_layers.Overlay)) { Owner = OverrideRedirectOwner(xwin) };
        _overrideRedirects.Add(frame);
        if (frame.Owner is { } owner && ViewOfWindow(owner) is { Canvas: { Scales: true } or { Terraces: true } } &&
            _canvasStates.TryGetValue(owner, out var state))
        {
            frame.Node.Matrix = state.Placement;
        }

        return new SceneTree(frame.Node);
    }

    private void DropOverrideRedirect(Basin.XWayland.XWaylandWindow xwin)
    {
        for (var i = _overrideRedirects.Count - 1; i >= 0; i--)
        {
            var frame = _overrideRedirects[i];
            if (ReferenceEquals(frame.Window, xwin))
            {
                _overrideRedirects.RemoveAt(i);
                frame.Node.Destroy();
            }
            else if (FindXWindow(xwin) is { } gone && ReferenceEquals(frame.Owner, gone))
            {
                frame.Owner = null;
                frame.Node.Matrix = RenderTransform.Identity;
            }
        }
    }

    private void SyncOverrideRedirects(IGrabTarget window, in RenderTransform placement)
    {
        for (var i = 0; i < _overrideRedirects.Count; i++)
        {
            var frame = _overrideRedirects[i];
            if (ReferenceEquals(frame.Owner, window))
            {
                frame.Node.Matrix = placement;
            }
        }
    }

    private XWindow? OverrideRedirectOwner(Basin.XWayland.XWaylandWindow xwin)
    {
        for (var parent = xwin.TransientFor; parent is not null; parent = parent.TransientFor)
        {
            if (FindXWindow(parent) is { } transientOwner)
            {
                return transientOwner;
            }

            if (ReferenceEquals(parent.TransientFor, xwin))
            {
                break;
            }
        }

        var client = XClientBase(xwin.WindowId);
        XWindow? nearest = null;
        var nearestDistance = long.MaxValue;
        foreach (var candidate in _xwindows)
        {
            if (XClientBase(candidate.XWin.WindowId) != client || candidate.Minimized)
            {
                continue;
            }

            var box = CanvasBoxOf(candidate);
            var dx = xwin.X < box.X ? box.X - xwin.X : xwin.X > box.Right ? xwin.X - box.Right : 0;
            var dy = xwin.Y < box.Y ? box.Y - xwin.Y : xwin.Y > box.Bottom ? xwin.Y - box.Bottom : 0;
            var distance = ((long)dx * dx) + ((long)dy * dy);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest is not null)
        {
            return nearest;
        }

        return _focusedX is { } focused && XClientBase(focused.XWin.WindowId) == client ? focused : null;
    }

    private static uint XClientBase(uint windowId) => windowId & ~XResourceIdMask;

    private RenderTransform ScalePlacementOf(IGrabTarget window, CanvasView canvas, CanvasWindowState? state)
    {
        if (_canvasSuspended || canvas.IsIdentity)
        {
            return state?.Blend.IsRunning == true
                ? CanvasScale.Blend(state.BlendFrom, RenderTransform.Identity, state.Blend.Current)
                : RenderTransform.Identity;
        }

        var target = RestingPlacement(window, canvas);
        if (state is null)
        {
            return target;
        }

        var box = CanvasBoxOf(window);

        if (state.Resizing)
        {
            var start = state.ResizeStart;
            var fixedX = state.ResizeFixedLeft ? start.Right : start.X;
            var fixedY = state.ResizeFixedTop ? start.Bottom : start.Y;
            target = CanvasScale.About(state.ResizeScale, fixedX, fixedY, state.ResizeScreenX, state.ResizeScreenY);
        }
        else if (state.Dragging)
        {
            var travel = Math.Sqrt(
                ((state.DragCursorX - state.DragStartX) * (state.DragCursorX - state.DragStartX)) +
                ((state.DragCursorY - state.DragStartY) * (state.DragCursorY - state.DragStartY)));
            var settle = Math.Max(0.0, 1.0 - (travel / GrabSettlePixels));
            var hand = canvas.Scale.HandPlacementFor(
                canvas.Map, box, window.X + state.DragGrabX, window.Y + state.DragGrabY,
                state.DragCursorX, state.DragCursorY, state.DragScaleCorrection * settle);
            if (!(hand.M11 == 1.0 && target.IsIdentity))
            {
                target = hand;
            }
        }

        return state.Blend.IsRunning ? CanvasScale.Blend(state.BlendFrom, target, state.Blend.Current) : target;
    }

    private void BeginCanvasGrab(IGrabTarget window, double x, double y)
    {
        _canvasScaleDrag = false;
        if (ViewOfWindow(window) is { Tag: OutputPolicy, Canvas.Terraces: true } terraced)
        {
            BeginTerraceGrab(window, terraced, x, y);
            return;
        }

        if (ViewOfWindow(window) is not { Tag: OutputPolicy, Canvas.Scales: true })
        {
            return;
        }

        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        var view = ViewOfWindow(window)!;
        var drawn = state.Placement.IsIdentity ? 1.0 : state.Placement.M11;
        state.Blend.Cancel();
        state.Dragging = true;
        state.DragGrabX = _grabX;
        state.DragGrabY = _grabY;
        state.Anchor = (_grabX, _grabY);
        state.DragCursorX = x;
        state.DragCursorY = y;
        state.DragStartX = x;
        state.DragStartY = y;
        state.DragScaleCorrection = drawn - view.Canvas.Scale.HandPlacementFor(
            view.Canvas.Map, CanvasBoxOf(window), window.X + _grabX, window.Y + _grabY, x, y).M11;
        (state.DragFieldX, state.DragFieldY) = view.Canvas.ToCanvasPoint(x, y);
        _canvasScaleDrag = true;
    }

    private RenderTransform RestingPlacement(IGrabTarget window, CanvasView canvas)
    {
        var placement = _canvasStates.TryGetValue(window, out var anchored) && anchored.Anchor is { } anchor
            ? canvas.Scale.AnchoredPlacementFor(canvas.Map, CanvasBoxOf(window), window.X + anchor.X, window.Y + anchor.Y)
            : canvas.Scale.PlacementFor(canvas.Map, CanvasBoxOf(window));
        return SnapPlacement(window, placement);
    }

    private RenderTransform SnapPlacement(IGrabTarget window, in RenderTransform placement)
    {
        if (placement.IsIdentity || window.EffectTree is not { } tree || ContentOf(window) is not { } content ||
            ViewOfWindow(window) is not { } view)
        {
            return placement;
        }

        var width = content.InputSurface?.Current.Width ?? 0;
        var height = content.InputSurface?.Current.Height ?? 0;
        var x = (double)tree.X;
        var y = (double)tree.Y;
        for (SceneNode? node = content; node is not null && !ReferenceEquals(node, tree); node = node.Parent)
        {
            x += node.X;
            y += node.Y;
        }

        if (width <= 0 || height <= 0)
        {
            return placement;
        }

        var scale = view.Output.Scale;
        var scaleX = Math.Round(placement.M11 * width * scale) / (width * scale);
        var scaleY = Math.Round(placement.M22 * height * scale) / (height * scale);
        var left = Math.Round(((placement.M11 * x) + placement.M13) * scale) / scale;
        var top = Math.Round(((placement.M22 * y) + placement.M23) * scale) / scale;
        return new RenderTransform(scaleX, 0, left - (scaleX * x), 0, scaleY, top - (scaleY * y), 0, 0, 1);
    }

    private SceneBuffer? ContentOf(IGrabTarget window)
    {
        var content = window switch
        {
            Window { SceneSurface: { IsDestroyed: false } surface } => surface.Content,
            XWindow { SceneSurface: { IsDestroyed: false } surface } => surface.Content,
            _ => null,
        };
        if (content is null || !_canvasStates.TryGetValue(window, out var state) || state.Node is not { IsDestroyed: false } node)
        {
            return content;
        }

        var best = default(SceneBuffer);
        var bestArea = 0L;
        LargestDmabuf(node, ref best, ref bestArea);
        return best ?? content;
    }

    private static void LargestDmabuf(SceneTree tree, ref SceneBuffer? best, ref long bestArea)
    {
        foreach (var child in tree.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            if (child is SceneTree subtree)
            {
                LargestDmabuf(subtree, ref best, ref bestArea);
            }
            else if (child is SceneBuffer { Buffer: { } buffer, InputSurface: { } surface } candidate && buffer.TryGetDmabuf(out _))
            {
                var area = (long)surface.Current.Width * surface.Current.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = candidate;
                }
            }
        }
    }

    private void BeginCanvasResize(IGrabTarget window, ResizeEdges edges)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy, Canvas: { Scales: true } or { Terraces: true } } resized ||
            !_canvasStates.TryGetValue(window, out var state) || state.Placement.IsIdentity)
        {
            return;
        }

        state.Blend.Cancel();
        var start = CanvasBoxOf(window);
        var left = (edges & ResizeEdges.Left) != ResizeEdges.None;
        var top = (edges & ResizeEdges.Top) != ResizeEdges.None;
        state.ResizeStart = start;
        state.ResizeFixedLeft = left;
        state.ResizeFixedTop = top;
        state.ResizeScale = state.Scale;
        (state.ResizeScreenX, state.ResizeScreenY) = state.Placement.Map(left ? start.Right : start.X, top ? start.Bottom : start.Y);
        state.ResizeEdges = edges;
        state.Resizing = true;
        state.ResizeHeldScale = resized.Canvas.Terraces && state.Region is not CanvasRegion.Shelf &&
            !(state.Region == CanvasRegion.Corner && resized.Canvas.Map.Separable);
        if (resized.Canvas.Terraces)
        {
            state.Anchor = ((left ? start.Right : start.X) - window.X, (top ? start.Bottom : start.Y) - window.Y);
        }
    }

    private bool ResizeCanvasScaled(IGrabTarget window, double x, double y, out double canvasX, out double canvasY)
    {
        canvasX = x;
        canvasY = y;
        if (!_canvasStates.TryGetValue(window, out var state) || !state.Resizing ||
            ViewOfWindow(window) is not { Tag: OutputPolicy, Canvas: { Scales: true } or { Terraces: true } } view)
        {
            return false;
        }

        var edges = state.ResizeEdges;
        if (view.Canvas.Terraces && state.ResizeHeldScale)
        {
            var heldFixedX = state.ResizeFixedLeft ? state.ResizeStart.Right : state.ResizeStart.X;
            var heldFixedY = state.ResizeFixedTop ? state.ResizeStart.Bottom : state.ResizeStart.Y;
            canvasX = heldFixedX + ((x - state.ResizeScreenX) / state.ResizeScale);
            canvasY = heldFixedY + ((y - state.ResizeScreenY) / state.ResizeScale);
            ApplyCanvas(window);
            return true;
        }

        if (view.Canvas.Terraces)
        {
            (state.ResizeScale, canvasX, canvasY) = view.Canvas.Scale.TerraceResizeCursor(
                view.Canvas.Map,
                state.ResizeStart,
                (edges & ResizeEdges.Left) != ResizeEdges.None,
                (edges & ResizeEdges.Right) != ResizeEdges.None,
                (edges & ResizeEdges.Top) != ResizeEdges.None,
                (edges & ResizeEdges.Bottom) != ResizeEdges.None,
                state.ResizeScreenX,
                state.ResizeScreenY,
                x,
                y,
                view.Canvas.Settings.ShelfMinScaleValue);
            ApplyCanvas(window);
            return true;
        }

        (state.ResizeScale, canvasX, canvasY) = view.Canvas.Scale.ResizeCursor(
            view.Canvas.Map,
            state.ResizeStart,
            (edges & ResizeEdges.Left) != ResizeEdges.None,
            (edges & ResizeEdges.Right) != ResizeEdges.None,
            (edges & ResizeEdges.Top) != ResizeEdges.None,
            (edges & ResizeEdges.Bottom) != ResizeEdges.None,
            state.ResizeScreenX,
            state.ResizeScreenY,
            x,
            y);
        ApplyCanvas(window);
        return true;
    }

    private bool DragCanvasScaled(IGrabTarget window, double x, double y)
    {
        if (!_canvasScaleDrag || !_canvasStates.TryGetValue(window, out var state) || !state.Dragging)
        {
            return false;
        }

        var (fieldX, fieldY) = ToCanvasPointAt(x, y);
        var movedX = x - state.DragCursorX;
        var movedY = y - state.DragCursorY;
        if (ViewOfWindow(window) is { Canvas: { Settings.DragValue: CanvasDrag.Grid } canvas })
        {
            var (stretchX, stretchY) = canvas.Map.AxisStretch(state.DragFieldX, state.DragFieldY);
            fieldX = state.DragFieldX + (movedX / stretchX);
            fieldY = state.DragFieldY + (movedY / stretchY);
        }

        state.DragFieldX = fieldX;
        state.DragFieldY = fieldY;
        state.DragCursorX = x;
        state.DragCursorY = y;
        var nextX = (int)Math.Round(fieldX - state.DragGrabX);
        var nextY = (int)Math.Round(fieldY - state.DragGrabY);
        if (nextX != window.X || nextY != window.Y)
        {
            window.MoveTo(nextX, nextY);
        }
        else
        {
            ApplyCanvas(window);
        }

        _effects.OnMoved((int)Math.Round(movedX), (int)Math.Round(movedY));
        return true;
    }

    private void EndCanvasGrab(IGrabTarget window)
    {
        _canvasScaleDrag = false;
        if (!_canvasStates.TryGetValue(window, out var state) || !(state.Dragging || state.Resizing))
        {
            return;
        }

        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view || window.EffectTree is not { IsDestroyed: false })
        {
            state.Dragging = false;
            state.Resizing = false;
            return;
        }

        var dropped = state.Dragging;
        state.Dragging = false;
        state.Resizing = false;
        var from = state.Placement;
        if (view.Canvas.Terraces)
        {
            EndTerraceGrab(window, view, state, from, dropped);
            return;
        }

        var rest = view.Canvas.Scales ? RestingPlacement(window, view.Canvas) : from;
        if (from != rest)
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

    private static void Bind(CanvasWarpTransform transform, CanvasView canvas)
    {
        transform.Left = canvas.Left;
        transform.Right = canvas.Right;
        transform.Top = canvas.Top;
        transform.Bottom = canvas.Bottom;
        transform.CornerRadius = canvas.Map.CornerRadius;
        transform.CornerTaper = canvas.Map.CornerTaper;
        transform.PreScale = 1.0;
        transform.PreStretchX = 1.0;
        transform.PreStretchY = 1.0;
        transform.Separable = canvas.Map.Separable;
    }

    private static Box UsableBox(OutputView view, in Box box) =>
        (view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea).Translated(box.X, box.Y);

    private static void AdoptStrays(SceneTree tree, SceneTransform node)
    {
        if (tree.Children.Count <= 1 || !ReferenceEquals(node.Parent, tree))
        {
            return;
        }

        var index = tree.Children.IndexOf(node);
        for (var i = index - 1; i >= 0; i--)
        {
            var below = tree.Children[i];
            below.Reparent(node);
            below.LowerToBottom();
        }

        while (tree.Children.Count > 1)
        {
            var above = tree.Children[^1];
            if (ReferenceEquals(above, node))
            {
                break;
            }

            above.Reparent(node);
        }
    }

    private void ForgetCanvas(IGrabTarget window)
    {
        _canvasStates.Remove(window);
        _canvasMotions.Remove(window);
    }

    internal double CanvasScaleOf(IGrabTarget window) =>
        _canvasStates.TryGetValue(window, out var state) && ViewOfWindow(window) is { Canvas: { Scales: true } or { Terraces: true } }
            ? state.Scale
            : 1.0;

    private bool BlocksConstraints(IGrabTarget window)
    {
        if (!_canvasStates.TryGetValue(window, out var state))
        {
            return false;
        }

        if (ViewOfWindow(window) is { Canvas.Terraces: true })
        {
            return state.Placement.IsIdentity ? state.Deformed : IsCanvasMoving(state);
        }

        if (ViewOfWindow(window) is not { Canvas.Scales: true })
        {
            return state.Deformed;
        }

        return IsCanvasMoving(state);
    }

    private static bool IsCanvasMoving(CanvasWindowState state) =>
        state.Dragging || state.Resizing || state.MotionX.IsRunning || state.MotionY.IsRunning || state.Blend.IsRunning ||
        state.ShelfPinned;

    private void AnnounceSurfaceScale(Surface surface, double scale) =>
        _fractionalScale.AnnounceScale(surface, scale * CanvasOfferFor(surface));

    private double CanvasOfferFor(Surface surface)
    {
        if (_canvasStates.Count == 0)
        {
            return 1.0;
        }

        var root = surface;
        while (root.SubsurfaceRole?.Parent is { } parent)
        {
            root = parent;
        }

        var xdg = root.RoleObject is XdgPopupWindow popup ? popup.Parent : null;
        while (xdg?.Role is XdgPopupWindow parentPopup)
        {
            xdg = parentPopup.Parent;
        }

        var toplevel = xdg?.Role as XdgToplevelWindow ?? root.RoleObject as XdgToplevelWindow;
        return toplevel is not null && FindWindow(toplevel) is { } window && _canvasStates.TryGetValue(window, out var state)
            ? state.OfferedScale
            : 1.0;
    }

    private void SyncScaleOffer(IGrabTarget target, CanvasWindowState state, CanvasView canvas)
    {
        if (target is not Window window || window.Tree is null)
        {
            return;
        }

        var size = window.GeometrySize;
        var xdg = window.Toplevel.Xdg;
        if (state.OfferedScale < 1.0 && !state.Resizing && !xdg.HasUnackedConfigure && size != state.OfferSize)
        {
            state.OfferRefused = true;
        }

        var wanted = (canvas.Scales || canvas.Terraces) && !state.OfferRefused && !_canvasSuspended && !IsCanvasMoving(state) &&
            !state.Placement.IsIdentity && state.Scale < 1.0
            ? Math.Round(state.Scale * 120) / 120
            : 1.0;
        if (wanted == state.OfferedScale || (state.OfferedScale < 1.0 && wanted < 1.0 && size != state.OfferSize))
        {
            return;
        }

        if (state.OfferedScale >= 1.0)
        {
            state.OfferSize = size;
        }

        state.OfferedScale = wanted;
        if (ViewOfWindow(window) is { } view)
        {
            _fractionalScale.AnnounceScale(window.Toplevel.Surface, view.Output.Scale * wanted);
        }

        var (width, height) = state.OfferSize;
        if (width > 0 && height > 0 &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized) &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
        {
            window.Toplevel.SetSize(width, height);
            window.Toplevel.RequestConfigure();
        }
    }

    internal bool IsCanvasDeformed(IGrabTarget window) =>
        _canvasStates.TryGetValue(window, out var state) && state.Deformed;

    internal Box ScreenBoxOf(IGrabTarget window)
    {
        var (width, height) = window.GeometrySize;
        var box = window switch
        {
            Window w => w.ScaleBox,
            _ => new Box(window.X, window.Y, Math.Max(width, 1), Math.Max(height, 1)),
        };
        return MapToScreen(window, box);
    }

    internal Box MapToScreen(IGrabTarget window, in Box box)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view || view.Canvas.IsIdentity)
        {
            return box;
        }

        var canvas = view.Canvas;
        if (canvas.Scales)
        {
            var placement = _canvasStates.TryGetValue(window, out var scaled)
                ? scaled.Placement
                : canvas.Scale.PlacementFor(canvas.Map, CanvasBoxOf(window));
            return canvas.Scale.DrawnBox(placement, box);
        }

        if (canvas.Terraces && _canvasStates.TryGetValue(window, out var terraced) && !terraced.Placement.IsIdentity)
        {
            return canvas.Scale.DrawnBox(terraced.Placement, box);
        }

        if (_canvasStates.TryGetValue(window, out var state) && ReferenceEquals(state.Transform.Left, canvas.Left))
        {
            var transform = state.Transform;
            return transform.MapBounds(box.Translated(-transform.SceneX, -transform.SceneY))
                .Translated(transform.SceneX, transform.SceneY);
        }

        return canvas.Map.MapBounds(box);
    }

    internal (double X, double Y) ToCanvasPointAt(double x, double y, IGrabTarget? window = null)
    {
        var view = ViewAt(x, y);
        if (view is not { Tag: OutputPolicy })
        {
            return (x, y);
        }

        var canvas = view.Canvas;
        if (window is not null && ViewOfWindow(window) is { Tag: OutputPolicy, Canvas.Terraces: true } &&
            _canvasStates.TryGetValue(window, out var terraced) && !terraced.Placement.IsIdentity &&
            terraced.Placement.TryInvert(out var shelfInverse))
        {
            return shelfInverse.Map(x, y);
        }

        if (window is not null && ViewOfWindow(window) is { Tag: OutputPolicy, Canvas.Scales: true })
        {
            if (_canvasStates.TryGetValue(window, out var scaled) && !scaled.Placement.IsIdentity &&
                scaled.Placement.TryInvert(out var inverse))
            {
                return inverse.Map(x, y);
            }

            return (x, y);
        }

        if (window is not null && _canvasStates.TryGetValue(window, out var state) &&
            ReferenceEquals(state.Transform.Left, canvas.Left))
        {
            return state.Transform.ToCanvasPoint(x, y);
        }

        return canvas.ToCanvasPoint(x, y);
    }

    internal (double X, double Y) ToScreenPointFor(IGrabTarget window, double x, double y)
    {
        var view = ViewOfWindow(window);
        return view is { Tag: OutputPolicy } ? view.Canvas.ToScreenPoint(x, y) : (x, y);
    }

    internal (double X, double Y) ToCanvasPointFor(IGrabTarget window, double x, double y)
    {
        var view = ViewOfWindow(window);
        return view is { Tag: OutputPolicy } ? view.Canvas.ToCanvasPoint(x, y) : (x, y);
    }

    private Box CanvasBoxOf(IGrabTarget window)
    {
        var (width, height) = window.GeometrySize;
        return window switch
        {
            Window { Frame: not null } w => w.FrameBox,
            XWindow { Frame: not null } x => x.FrameBox,
            Window w => w.ScaleBox,
            _ => new Box(window.X, window.Y, Math.Max(width, 1), Math.Max(height, 1)),
        };
    }

    private static string NameOf(IGrabTarget window) => window switch
    {
        Window w => w.Toplevel.AppId,
        XWindow x => x.XWin.Class,
        _ => "?",
    };

    private long CanvasAnimationNanos(OutputView view) => (long)view.Canvas.Settings.AnimationMillis * 1_000_000;

    private static string ParkName(CanvasSide side) => side switch
    {
        CanvasSide.Left => "left",
        CanvasSide.Right => "right",
        CanvasSide.Top => "up",
        _ => "down",
    };

    private static string ZoneName(CanvasSide side) => side switch
    {
        CanvasSide.Left => "left",
        CanvasSide.Right => "right",
        CanvasSide.Top => "top",
        _ => "bottom",
    };

    internal void Park(IGrabTarget window, CanvasSide side)
    {
        if (window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return;
        }

        var canvas = view.Canvas;
        var warp = side switch
        {
            CanvasSide.Left => canvas.Left,
            CanvasSide.Right => canvas.Right,
            CanvasSide.Top => canvas.Top,
            CanvasSide.Bottom => canvas.Bottom,
            _ => null,
        };
        if (warp is null)
        {
            return;
        }

        if (warp.IsIdentity)
        {
            _report.Line($"PARK {NameOf(window)} refused: no {ZoneName(side)} zone");
            return;
        }

        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        state.Home ??= (window.X, window.Y);
        state.Anchor = null;
        var box = CanvasBoxOf(window);
        var vertical = (side & CanvasSide.Vertical) != 0;
        var across = vertical
            ? (canvas.Left.ContainsCanvas(box.X) ? canvas.Left : canvas.Right.ContainsCanvas(box.Right) ? canvas.Right : null)
            : (canvas.Top.ContainsCanvas(box.Y) ? canvas.Top : canvas.Bottom.ContainsCanvas(box.Bottom) ? canvas.Bottom : null);
        var nanos = CanvasAnimationNanos(view);
        if (across is not null)
        {
            var (cornerX, cornerY) = ParkTargets(canvas, window, box, vertical ? across : warp, vertical ? warp : across);
            BeginCanvasMotion(window, state, vertical: false, cornerX, nanos);
            BeginCanvasMotion(window, state, vertical: true, cornerY, nanos);
            _report.Line($"PARK {NameOf(window)} {ParkName(side)} to={cornerX} y={cornerY}{ParkScale(canvas, window, box, cornerX, cornerY)} corner");
            return;
        }

        var (targetX, targetY) = ParkTargets(canvas, window, box, vertical ? null : warp, vertical ? warp : null);
        var target = vertical ? targetY : targetX;
        BeginCanvasMotion(window, state, vertical, target, nanos);
        _report.Line($"PARK {NameOf(window)} {ParkName(side)} to={target}{ParkScale(canvas, window, box, targetX, targetY)}");
    }

    private (int X, int Y) ParkTargets(CanvasView canvas, IGrabTarget window, in Box box, CanvasWarp? side, CanvasWarp? end)
    {
        var offsetX = window.X - box.X;
        var offsetY = window.Y - box.Y;
        if (canvas.Terraces && (side is not null || end is not null))
        {
            return TerraceParkTargets(canvas, window, box, side, end);
        }

        if (canvas.Scales && (side is not null || end is not null))
        {
            return HandParkTarget(canvas, window, box, side, end);
        }

        if (side is not null && end is not null)
        {

            var reach = canvas.Map.RimDiagonal;
            var edgeX = side.Seam + (side.Direction * reach * side.Extension);
            var edgeY = end.Seam + (end.Direction * reach * end.Extension);
            return (
                (int)Math.Round(side.Direction < 0 ? edgeX : edgeX - box.Width) + offsetX,
                (int)Math.Round(end.Direction < 0 ? edgeY : edgeY - box.Height) + offsetY);
        }

        var x = window.X;
        var y = window.Y;
        if (side is not null)
        {
            x = (canvas.Scales
                ? canvas.Scale.ParkTarget(canvas.Map, side, box)
                : side.Direction < 0 ? side.FarEdge : side.FarEdge - box.Width) + offsetX;
        }

        if (end is not null)
        {
            y = (canvas.Scales
                ? canvas.Scale.ParkTarget(canvas.Map, end, box)
                : end.Direction < 0 ? end.FarEdge : end.FarEdge - box.Height) + offsetY;
        }

        return (x, y);
    }

    private (int X, int Y) HandParkTarget(CanvasView canvas, IGrabTarget window, in Box box, CanvasWarp? side, CanvasWarp? end)
    {
        var anchorX = box.X + (box.Width / 2.0);
        var anchorY = box.Y + (box.Height / 2.0);
        var floor = canvas.Scale.MinScale;
        var (screenX, screenY) = canvas.Map.ToScreenPoint(anchorX, anchorY);
        if (side is not null)
        {
            screenX = side.Direction > 0
                ? side.ScreenEdge - (floor * (box.Right - anchorX))
                : side.ScreenEdge + (floor * (anchorX - box.X));
        }

        if (end is not null)
        {
            screenY = end.Direction > 0
                ? end.ScreenEdge - (floor * (box.Bottom - anchorY))
                : end.ScreenEdge + (floor * (anchorY - box.Y));
        }

        var (canvasX, canvasY) = canvas.Map.ToCanvasPoint(screenX, screenY);
        if (_canvasStates.TryGetValue(window, out var state))
        {
            state.Anchor = (anchorX - window.X, anchorY - window.Y);
        }

        return (
            (int)Math.Round(canvasX - (anchorX - window.X)),
            (int)Math.Round(canvasY - (anchorY - window.Y)));
    }

    private static string ParkScale(CanvasView canvas, IGrabTarget window, in Box box, int x, int y)
    {
        var moved = box.Translated(x - window.X, y - window.Y);
        if (canvas.Terraces)
        {
            return $" scale={canvas.Scale.ShelfScaleFor(canvas.Map, moved, canvas.Settings.ShelfMinScaleValue):F3}";
        }

        if (!canvas.Scales)
        {
            return string.Empty;
        }

        var placement = canvas.Scale.AnchoredPlacementFor(
            canvas.Map, moved, moved.X + (moved.Width / 2.0), moved.Y + (moved.Height / 2.0));
        return $" scale={placement.M11:F3}";
    }

    private static (CanvasWarp? Side, CanvasWarp? End) ZonesHolding(CanvasView canvas, in Box box) =>
        (canvas.Left.ContainsCanvas(box.X) ? canvas.Left : canvas.Right.ContainsCanvas(box.Right) ? canvas.Right : null,
         canvas.Top.ContainsCanvas(box.Y) ? canvas.Top : canvas.Bottom.ContainsCanvas(box.Bottom) ? canvas.Bottom : null);

    internal void Recall(IGrabTarget window)
    {
        if (window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return;
        }

        var canvas = view.Canvas;
        _ = _canvasStates.TryGetValue(window, out var state);
        var box = CanvasBoxOf(window);
        int? targetX = null;
        int? targetY = null;
        if (state?.Home is { } home)
        {
            targetX = home.X;
            targetY = home.Y;
        }
        else
        {
            if (canvas.Left.ContainsCanvas(box.X))
            {
                targetX = canvas.Left.Seam + (window.X - box.X);
            }
            else if (canvas.Right.ContainsCanvas(box.Right))
            {
                targetX = canvas.Right.Seam - box.Width + (window.X - box.X);
            }

            if (canvas.Top.ContainsCanvas(box.Y))
            {
                targetY = canvas.Top.Seam + (window.Y - box.Y);
            }
            else if (canvas.Bottom.ContainsCanvas(box.Bottom))
            {
                targetY = canvas.Bottom.Seam - box.Height + (window.Y - box.Y);
            }

            if (targetX is null && targetY is null)
            {
                return;
            }
        }

        state ??= new CanvasWindowState();
        _canvasStates[window] = state;
        state.Home = null;
        state.Anchor = null;
        var nanos = CanvasAnimationNanos(view);
        if (targetX is { } x)
        {
            BeginCanvasMotion(window, state, vertical: false, x, nanos);
        }

        if (targetY is { } y)
        {
            BeginCanvasMotion(window, state, vertical: true, y, nanos);
        }

        _report.Line($"RECALL {NameOf(window)} to={targetX ?? window.X} y={targetY ?? window.Y}");
    }

    private void BeginCanvasMotion(IGrabTarget window, CanvasWindowState state, bool vertical, int target, long nanos)
    {
        state.ShelfPinned = false;
        var motion = vertical ? state.MotionY : state.MotionX;
        motion.Begin(vertical ? window.Y : window.X, target, nanos);
        if (!motion.IsRunning)
        {
            window.MoveTo(vertical ? window.X : target, vertical ? target : window.Y);
            return;
        }

        if (!_canvasMotions.Contains(window))
        {
            _canvasMotions.Add(window);
        }

        ScheduleEffectRepaint();
    }

    private bool StepCanvasMotions(in FrameTick tick)
    {
        if (_canvasMotions.Count == 0)
        {
            return false;
        }

        _canvasMotionsDone.Clear();
        foreach (var window in _canvasMotions)
        {
            if (!_canvasStates.TryGetValue(window, out var state) || window.EffectTree is not { IsDestroyed: false })
            {
                _canvasMotionsDone.Add(window);
                continue;
            }

            var running = false;
            var blended = false;
            if (state.Blend.IsRunning)
            {
                running |= state.Blend.Step(tick, out _);
                blended = true;
            }

            var nextX = window.X;
            var nextY = window.Y;
            if (state.MotionX.IsRunning)
            {
                running |= state.MotionX.Step(tick, out var x);
                nextX = (int)Math.Round(x);
            }

            if (state.MotionY.IsRunning)
            {
                running |= state.MotionY.Step(tick, out var y);
                nextY = (int)Math.Round(y);
            }

            if (nextX != window.X || nextY != window.Y)
            {
                window.MoveTo(nextX, nextY);
            }
            else if (blended)
            {
                ApplyCanvas(window);
            }

            if (!running)
            {
                _canvasMotionsDone.Add(window);
            }
        }

        foreach (var window in _canvasMotionsDone)
        {
            _canvasMotions.Remove(window);
            if (window.EffectTree is { IsDestroyed: false })
            {
                ApplyCanvas(window);
            }
        }

        _canvasMotionsDone.Clear();
        if (_canvasMotions.Count == 0 && _canvasClosing)
        {
            _canvasClosing = false;
            LayoutCanvasAll();
        }

        return _canvasMotions.Count > 0;
    }

    internal void SetCanvasEnabled(bool enabled)
    {
        _canvasOverride = enabled;
        if (enabled)
        {
            _canvasClosing = false;
            LayoutCanvasAll();
            _report.Line("CANVAS on");
            return;
        }

        var recalled = 0;
        foreach (var (window, state) in _canvasStates)
        {
            if (state.Deformed || (window.EffectTree is { IsDestroyed: false } && IsOffCentre(window)))
            {
                Recall(window);
                recalled++;
            }
        }

        _report.Line($"CANVAS off recalled={recalled}");
        if (_canvasMotions.Count > 0)
        {
            _canvasClosing = true;
            return;
        }

        LayoutCanvasAll();
    }

    private bool IsOffCentre(IGrabTarget window)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return false;
        }

        var box = CanvasBoxOf(window);
        var canvas = view.Canvas;
        return canvas.Left.ContainsCanvas(box.X) || canvas.Right.ContainsCanvas(box.Right) ||
            canvas.Top.ContainsCanvas(box.Y) || canvas.Bottom.ContainsCanvas(box.Bottom);
    }

    internal void SuspendCanvas()
    {
        if (_canvasSuspended)
        {
            return;
        }

        _canvasSuspended = true;
        BlendScaledWindows(_effects.SwitcherFlyNanos);
        ApplyCanvasToAll();
    }

    private void BlendScaledWindows(long nanos)
    {
        foreach (var (window, state) in _canvasStates)
        {
            if (window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) is not { } view)
            {
                continue;
            }

            if (view.Canvas.Terraces)
            {
                BlendTerraceWindow(window, view.Canvas, state, nanos);
                continue;
            }

            if (!view.Canvas.Scales)
            {
                continue;
            }

            var target = _canvasSuspended ? RenderTransform.Identity : RestingPlacement(window, view.Canvas);
            if (state.Placement == target)
            {
                continue;
            }

            state.BlendFrom = state.Placement;
            state.Blend.Cancel();
            state.Blend.Begin(0.0, 1.0, nanos);
            if (state.Blend.IsRunning && !_canvasMotions.Contains(window))
            {
                _canvasMotions.Add(window);
            }
        }

        ScheduleEffectRepaint();
    }

    internal void ResumeCanvas()
    {
        if (!_canvasSuspended)
        {
            return;
        }

        _canvasSuspended = false;
        BlendScaledWindows(_effects.SwitcherFlyNanos);
        ApplyCanvasToAll();
    }

    private void ClearCanvasHomeAfterDrop(IGrabTarget window)
    {
        if (_canvasStates.TryGetValue(window, out var state) && IsOffCentre(window))
        {
            state.Home = null;
        }
    }

    internal Box FlatArea(OutputView view, in Box usable)
    {
        if (view.Tag is not OutputPolicy || view.Canvas.IsIdentity)
        {
            return usable;
        }

        var box = _layout.BoxOf(view.Output);
        var canvas = view.Canvas;
        var x = canvas.Left.IsIdentity ? usable.X : Math.Max(usable.X, canvas.Left.Seam - box.X);
        var end = canvas.Right.IsIdentity ? usable.Right : Math.Min(usable.Right, canvas.Right.Seam - box.X);
        var y = canvas.Top.IsIdentity ? usable.Y : Math.Max(usable.Y, canvas.Top.Seam - box.Y);
        var bottom = canvas.Bottom.IsIdentity ? usable.Bottom : Math.Min(usable.Bottom, canvas.Bottom.Seam - box.Y);
        return new Box(x, y, Math.Max(1, end - x), Math.Max(1, bottom - y));
    }

    private void ReportCanvasWhere(IGrabTarget window)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view || view.Canvas.IsIdentity)
        {
            return;
        }

        var screen = ScreenBoxOf(window);
        _report.Line(
            $"CANVASWIN {NameOf(window)} canvas={window.X},{window.Y} screen={screen.X},{screen.Y}"
            + $" width={screen.Width} height={screen.Height}"
            + $" deformed={(IsCanvasDeformed(window) ? "yes" : "no")}"
            + $" mode={CanvasSetting.NameOf(view.Canvas.Mode)} scale={(_canvasStates.TryGetValue(window, out var scaled) ? scaled.Scale : 1.0):F3}"
            + (view.Canvas.Terraces ? TerraceWhere(window) : string.Empty)
            + $" home={(_canvasStates.TryGetValue(window, out var state) && state.Home is { } home ? $"{home.X},{home.Y}" : "none")}");
    }

    private IGrabTarget? FocusedGrabTarget() => _focused is { Tree: not null } focused
        ? focused
        : _focusedX is { Framable: true } focusedX ? focusedX : null;

    private bool CanvasActiveAnywhere()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is OutputPolicy && view.Canvas.Enabled)
            {
                return true;
            }
        }

        return false;
    }

    private static (double X, double Y) OwnerLocal(Frame frame, IGrabTarget owner, double x, double y)
    {
        if (frame.Tree.TryMapSceneToLocal(x, y, out var localX, out var localY))
        {
            return (localX + frame.Tree.X, localY + frame.Tree.Y);
        }

        return (x - owner.X, y - owner.Y);
    }

    private void PlaceRuleCanvas(IGrabTarget window, Rule? rule)
    {
        if (rule?.Canvas is not { } sides)
        {
            return;
        }

        if ((sides & CanvasSide.Horizontal) is var horizontal and not CanvasSide.None)
        {
            Park(window, horizontal);
        }

        if ((sides & CanvasSide.Vertical) is var vertical and not CanvasSide.None)
        {
            Park(window, vertical);
        }
    }
}
