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
        var zone = (int)Math.Round(settings.ZoneFraction * box.Width);
        var extension = (int)Math.Round(settings.ExtensionFraction * box.Width);
        var usable = enabled && !fullscreen && zone > 0 && extension > 0;
        var leftActive = usable && !HasNeighbour(view.Output, box, left: true);
        var rightActive = usable && !HasNeighbour(view.Output, box, left: false);
        var center = box.Y + (box.Height / 2.0);
        var changed = canvas.Left.Layout(
            box.X + zone, -1, leftActive ? zone : 0, leftActive ? extension : 0, settings.EdgeScaleValue,
            settings.SlopeValue, center);
        changed |= canvas.Right.Layout(
            box.Right - zone, 1, rightActive ? zone : 0, rightActive ? extension : 0, settings.EdgeScaleValue,
            settings.SlopeValue, center);
        changed |= LayoutCanvasGrid(view, box, enabled && settings.GridMode != CanvasGridMode.Never);
        if (!changed)
        {
            return;
        }

        canvas.Generation++;
        var active = !canvas.Left.IsIdentity ? canvas.Left : !canvas.Right.IsIdentity ? canvas.Right : null;
        BasinReport.Line(
            $"CANVAS view={ViewIndex(view)} left={canvas.Left.ZoneWidth} right={canvas.Right.ZoneWidth}"
            + $" extension={active?.Extension ?? 0} edge_scale={(active?.EdgeScale ?? settings.EdgeScaleValue):F3}"
            + $" exponent={(active?.Exponent ?? 0):F2} slope={(active?.Slope ?? 0):F2}");
        ApplyCanvasToView(view);
        canvas.Grid?.NotifyMeshChanged();
        view.Scheduler?.ScheduleRepaint();
    }

    private bool HasNeighbour(IOutput output, in Box box, bool left)
    {
        foreach (var (other, _) in _layout.Outputs)
        {
            if (ReferenceEquals(other, output))
            {
                continue;
            }

            var otherBox = _layout.BoxOf(other);
            if (otherBox.IsEmpty || otherBox.Bottom <= box.Y || otherBox.Y >= box.Bottom)
            {
                continue;
            }

            if (left ? otherBox.Right == box.X : otherBox.X == box.Right)
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
        var source = canvas.GridSource ??= new CanvasGridSource { Left = canvas.Left, Right = canvas.Right };
        var color = settings.GridRenderColor;
        var alpha = GridAlphaFor(settings);
        var changed = source.CellSize != settings.GridCellSize || source.Color != color || source.Alpha != alpha;
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
        var bestOverlap = 0;
        for (var i = 0; i < Views.Count; i++)
        {
            var candidate = Views[i];
            if (candidate.Tag is not OutputPolicy || !_layout.Contains(candidate.Output))
            {
                continue;
            }

            var box = _layout.BoxOf(candidate.Output);
            if (window.Y >= box.Bottom || windowBottom <= box.Y)
            {
                continue;
            }

            var canvas = candidate.Canvas;
            var spanLeft = canvas.Left.IsIdentity ? box.X : canvas.Left.FarEdge;
            var spanRight = canvas.Right.IsIdentity ? box.Right : canvas.Right.FarEdge;
            var overlap = Math.Min(windowRight, spanRight) - Math.Max(window.X, spanLeft);
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
            probe.Left = canvas.Left;
            probe.Right = canvas.Right;
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
        transform.Left = canvas.Left;
        transform.Right = canvas.Right;
        transform.SceneX = tree.X;
        transform.SceneY = tree.Y;
        transform.CellSize = canvas.Settings.MeshCellSize;
        transform.MaxFan = FanCeiling(window, view);
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

    private double FanCeiling(IGrabTarget window, OutputView view)
    {
        var box = _layout.BoxOf(view.Output);
        var center = view.Canvas.Left.Center;
        var frame = CanvasBoxOf(window);
        var ceiling = double.PositiveInfinity;
        if (frame.Y < center)
        {
            ceiling = Math.Min(ceiling, (center - box.Y) / (center - frame.Y));
        }

        if (frame.Bottom > center)
        {
            ceiling = Math.Min(ceiling, (box.Bottom - center) / (frame.Bottom - center));
        }

        return Math.Max(1.0, ceiling);
    }

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
        var left = (int)Math.Floor(canvas.ToScreen(box.X));
        var right = (int)Math.Ceiling(canvas.ToScreen(box.Right));
        double topLeft, topRight, bottomLeft, bottomRight;
        if (_canvasStates.TryGetValue(window, out var state) && ReferenceEquals(state.Transform.Left, canvas.Left))
        {
            var transform = state.Transform;
            topLeft = transform.ToScreenY(box.X, box.Y);
            topRight = transform.ToScreenY(box.Right, box.Y);
            bottomLeft = transform.ToScreenY(box.X, box.Bottom);
            bottomRight = transform.ToScreenY(box.Right, box.Bottom);
        }
        else
        {
            topLeft = canvas.ToScreenY(box.X, box.Y);
            topRight = canvas.ToScreenY(box.Right, box.Y);
            bottomLeft = canvas.ToScreenY(box.X, box.Bottom);
            bottomRight = canvas.ToScreenY(box.Right, box.Bottom);
        }

        var top = (int)Math.Floor(Math.Min(topLeft, topRight));
        var bottom = (int)Math.Ceiling(Math.Max(bottomLeft, bottomRight));
        return new Box(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
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
            return (canvas.ToCanvas(x), state.Transform.ToCanvasY(x, y));
        }

        return (canvas.ToCanvas(x), canvas.ToCanvasY(x, y));
    }

    internal double ToCanvasAt(double x, double y)
    {
        var view = ViewAt(x, y);
        return view is { Tag: OutputPolicy } ? view.Canvas.ToCanvas(x) : x;
    }

    internal double ToScreenFor(IGrabTarget window, double x)
    {
        var view = ViewOfWindow(window);
        return view is { Tag: OutputPolicy } ? view.Canvas.ToScreen(x) : x;
    }

    internal double ToCanvasFor(IGrabTarget window, double x)
    {
        var view = ViewOfWindow(window);
        return view is { Tag: OutputPolicy } ? view.Canvas.ToCanvas(x) : x;
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

    internal void Park(IGrabTarget window, CanvasSide side)
    {
        if (window.EffectTree is not { IsDestroyed: false } || ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return;
        }

        var canvas = view.Canvas;
        var warp = side == CanvasSide.Left ? canvas.Left : canvas.Right;
        if (warp.IsIdentity)
        {
            BasinReport.Line($"PARK {NameOf(window)} refused: no {(side == CanvasSide.Left ? "left" : "right")} zone");
            return;
        }

        if (!_canvasStates.TryGetValue(window, out var state))
        {
            state = new CanvasWindowState();
            _canvasStates[window] = state;
        }

        state.Home ??= (window.X, window.Y);
        var box = CanvasBoxOf(window);
        var target = side == CanvasSide.Left
            ? warp.FarEdge + (window.X - box.X)
            : warp.FarEdge - box.Width + (window.X - box.X);
        BeginCanvasMotion(window, state, target, CanvasAnimationNanos(view));
        BasinReport.Line($"PARK {NameOf(window)} {(side == CanvasSide.Left ? "left" : "right")} to={target}");
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
        int target;
        if (state?.Home is { } home)
        {
            target = home.X;
        }
        else if (canvas.Left.ContainsCanvas(box.X))
        {
            target = canvas.Left.Seam + (window.X - box.X);
        }
        else if (canvas.Right.ContainsCanvas(box.Right))
        {
            target = canvas.Right.Seam - box.Width + (window.X - box.X);
        }
        else
        {
            return;
        }

        state ??= new CanvasWindowState();
        _canvasStates[window] = state;
        state.Home = null;
        BeginCanvasMotion(window, state, target, CanvasAnimationNanos(view));
        BasinReport.Line($"RECALL {NameOf(window)} to={target}");
    }

    private void BeginCanvasMotion(IGrabTarget window, CanvasWindowState state, int target, long nanos)
    {
        state.Motion.Begin(window.X, target, nanos);
        if (!state.Motion.IsRunning)
        {
            window.MoveTo(target, window.Y);
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

            var running = state.Motion.Step(tick, out var x);
            var next = (int)Math.Round(x);
            if (next != window.X)
            {
                window.MoveTo(next, window.Y);
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
            BasinReport.Line("CANVAS on");
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

        BasinReport.Line($"CANVAS off recalled={recalled}");
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
        return view.Canvas.Left.ContainsCanvas(box.X) || view.Canvas.Right.ContainsCanvas(box.Right);
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
        var left = canvas.Left.IsIdentity ? 0 : canvas.Left.ZoneWidth;
        var right = canvas.Right.IsIdentity ? 0 : canvas.Right.ZoneWidth;
        var x = Math.Max(usable.X, left);
        var end = Math.Min(usable.Right, box.Width - right);
        return new Box(x, usable.Y, Math.Max(1, end - x), usable.Height);
    }

    private void ReportCanvasWhere(IGrabTarget window)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view || view.Canvas.IsIdentity)
        {
            return;
        }

        var screen = ScreenBoxOf(window);
        BasinReport.Line(
            $"CANVASWIN {NameOf(window)} canvas={window.X} screen={screen.X} width={screen.Width}"
            + $" deformed={(IsCanvasDeformed(window) ? "yes" : "no")}"
            + $" home={(_canvasStates.TryGetValue(window, out var state) && state.Home is { } home ? home.X.ToString() : "none")}");
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
        if (rule?.Canvas is { } side)
        {
            Park(window, side);
        }
    }
}
