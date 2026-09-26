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

    private readonly Dictionary<IGrabTarget, CanvasWindowState> _canvasStates = [];
    private readonly List<IGrabTarget> _canvasMotions = [];
    private readonly List<IGrabTarget> _canvasMotionsDone = [];
    private bool _canvasSuspended;
    private bool? _canvasOverride;
    private bool _canvasClosing;
    private bool _canvasGridDragging;

    private bool CanvasWanted(OutputView view) => _canvasOverride ?? view.Canvas.Settings.Enabled;

    private void LayoutCanvasAll()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            LayoutCanvas(view);
        }
    }

    private void LayoutCanvas(OutputView view)
    {
        if (view.Tag is not OutputPolicy || !_layout.Contains(view.Output))
        {
            return;
        }

        var canvas = view.Canvas;
        var settings = _config.CanvasFor(view.Output.Name, _log);
        canvas.Settings = settings;
        var box = _layout.BoxOf(view.Output);
        var enabled = CanvasWanted(view) && !box.IsEmpty;
        canvas.Enabled = enabled;
        var fullscreen = AnyFullscreenOn(view);
        var usable = UsableBox(view, box);
        var sides = settings.SideSet;
        var zone = (int)Math.Round(settings.ZoneFraction * box.Width);
        var extension = (int)Math.Round(settings.ExtensionFraction * box.Width);
        var zoneY = (int)Math.Round(settings.ZoneFraction * box.Height);
        var extensionY = (int)Math.Round(settings.ExtensionFraction * box.Height);
        var open = enabled && !fullscreen;
        var horizontal = open && zone > 0 && extension > 0;
        var vertical = open && zoneY > 0 && extensionY > 0;
        var leftActive = horizontal && sides.HasFlag(CanvasSide.Left) && !HasNeighbour(view.Output, box, CanvasSide.Left);
        var rightActive = horizontal && sides.HasFlag(CanvasSide.Right) && !HasNeighbour(view.Output, box, CanvasSide.Right);
        var topActive = vertical && sides.HasFlag(CanvasSide.Top) && !HasNeighbour(view.Output, box, CanvasSide.Top);
        var bottomActive = vertical && sides.HasFlag(CanvasSide.Bottom) && !HasNeighbour(view.Output, box, CanvasSide.Bottom);
        var centerY = usable.Y + (usable.Height / 2.0);
        var centerX = usable.X + (usable.Width / 2.0);
        var changed = canvas.Left.Layout(
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
        var active = !canvas.Left.IsIdentity ? canvas.Left : !canvas.Right.IsIdentity ? canvas.Right
            : !canvas.Top.IsIdentity ? canvas.Top : !canvas.Bottom.IsIdentity ? canvas.Bottom : null;
        _report.Line(
            $"CANVAS view={ViewIndex(view)} left={canvas.Left.ZoneWidth} right={canvas.Right.ZoneWidth}"
            + $" top={canvas.Top.ZoneWidth} bottom={canvas.Bottom.ZoneWidth} corner={(canvas.Map.CornerTaper ? "taper" : "radius")}:{canvas.Map.CornerRadius:F2}"
            + $" extension={active?.Extension ?? 0} edge_scale={(active?.EdgeScale ?? settings.EdgeScaleValue):F3}"
            + $" exponent={(active?.Exponent ?? 0):F2} slope={(active?.Slope ?? 0):F2}");
        KeepParkedInside(view);
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
        var changed = source.CellSize != settings.GridCellSize || source.Color != color || source.Alpha != alpha ||
            source.CornerRadius != canvas.Map.CornerRadius || source.CornerTaper != canvas.Map.CornerTaper;
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
            var spanLeft = canvas.Left.IsIdentity ? box.X : canvas.Left.FarEdge;
            var spanRight = canvas.Right.IsIdentity ? box.Right : canvas.Right.FarEdge;
            var spanTop = canvas.Top.IsIdentity ? box.Y : canvas.Top.FarEdge;
            var spanBottom = canvas.Bottom.IsIdentity ? box.Bottom : canvas.Bottom.FarEdge;
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

    private void KeepParkedInside(OutputView view)
    {
        var canvas = view.Canvas;
        foreach (var (window, state) in _canvasStates)
        {
            if (state.Home is null || state.MotionX.IsRunning || state.MotionY.IsRunning ||
                window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) != view)
            {
                continue;
            }

            var box = CanvasBoxOf(window);
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

    private static void Bind(CanvasWarpTransform transform, CanvasView canvas)
    {
        transform.Left = canvas.Left;
        transform.Right = canvas.Right;
        transform.Top = canvas.Top;
        transform.Bottom = canvas.Bottom;
        transform.CornerRadius = canvas.Map.CornerRadius;
        transform.CornerTaper = canvas.Map.CornerTaper;
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
        var box = CanvasBoxOf(window);
        var vertical = (side & CanvasSide.Vertical) != 0;
        var across = vertical
            ? (canvas.Left.ContainsCanvas(box.X) ? canvas.Left : canvas.Right.ContainsCanvas(box.Right) ? canvas.Right : null)
            : (canvas.Top.ContainsCanvas(box.Y) ? canvas.Top : canvas.Bottom.ContainsCanvas(box.Bottom) ? canvas.Bottom : null);
        if (across is not null)
        {
            var (cornerX, cornerY) = ParkInCorner(window, state, box, vertical ? across : warp, vertical ? warp : across, view);
            _report.Line($"PARK {NameOf(window)} {ParkName(side)} to={cornerX} y={cornerY} corner");
            return;
        }

        var target = side switch
        {
            CanvasSide.Left => warp.FarEdge + (window.X - box.X),
            CanvasSide.Right => warp.FarEdge - box.Width + (window.X - box.X),
            CanvasSide.Top => warp.FarEdge + (window.Y - box.Y),
            _ => warp.FarEdge - box.Height + (window.Y - box.Y),
        };
        BeginCanvasMotion(window, state, vertical, target, CanvasAnimationNanos(view));
        _report.Line($"PARK {NameOf(window)} {ParkName(side)} to={target}");
    }

    private (int X, int Y) ParkInCorner(IGrabTarget window, CanvasWindowState state, in Box box, CanvasWarp side, CanvasWarp end, OutputView view)
    {
        var reach = view.Canvas.Map.RimDiagonal;
        var edgeX = side.Seam + (side.Direction * reach * side.Extension);
        var edgeY = end.Seam + (end.Direction * reach * end.Extension);
        var targetX = (int)Math.Round(side.Direction < 0 ? edgeX : edgeX - box.Width) + (window.X - box.X);
        var targetY = (int)Math.Round(end.Direction < 0 ? edgeY : edgeY - box.Height) + (window.Y - box.Y);
        var nanos = CanvasAnimationNanos(view);
        BeginCanvasMotion(window, state, vertical: false, targetX, nanos);
        BeginCanvasMotion(window, state, vertical: true, targetY, nanos);
        return (targetX, targetY);
    }

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

            if (!running)
            {
                _canvasMotionsDone.Add(window);
            }
        }

        foreach (var window in _canvasMotionsDone)
        {
            _canvasMotions.Remove(window);
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
        ApplyCanvasToAll();
    }

    internal void ResumeCanvas()
    {
        if (!_canvasSuspended)
        {
            return;
        }

        _canvasSuspended = false;
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
