using Basin;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class EffectsPolicy : IDisposable
{
    private static readonly AnimationDuration OpenClose = new(250);
    private static readonly AnimationDuration Fire = new(450);
    private static readonly AnimationDuration Glide = new(160);
    private static readonly AnimationDuration Sheet = new(300);
    private static readonly AnimationDuration FallApart = new(1000);
    private static readonly AnimationDuration Minimize = new(250);
    private static readonly AnimationDuration Highlight = new(150);
    private static readonly AnimationDuration DimFade = new(160);

    private readonly OpenCloseRunner _runner = new();
    private readonly Dictionary<SceneTree, (TransformStack Stack, WobblyEffect Wobbly)> _wobblies = [];
    private readonly Dictionary<SceneTree, MinimizeRun> _minimizing = [];
    private readonly Dictionary<SceneTree, StretchRun> _stretches = [];
    private readonly HighlightWindowEffect _highlight = new(0.15);
    private readonly List<SceneTree> _highlighted = [];
    private readonly SwitcherEffect _switcher = new();
    private readonly List<TransformStack> _switcherStacks = [];
    private readonly SlideTransition _slide = new();
    private readonly SlideBackEffect _slideBack = new();
    private readonly SlidingNotificationsEffect _notifications = new();
    private Action? _slideDone;
    private WobblyEffect? _grabbed;
    private FrameTick _lastTick;

    public bool WobblyEnabled { get; set; }

    public string? OpenKind { get; set; }

    public string? CloseKind { get; set; }

    public string? MinimizeKind { get; set; }

    public bool SwitcherEnabled { get; set; }

    public bool HighlightEnabled { get; set; }

    public bool SlideBackEnabled { get; set; }

    public bool StretchEnabled { get; set; }

    public bool NotificationsEnabled { get; set; }

    public DimInactiveEffect? Dim { get; set; }

    public Action<double>? DimChanged { get; set; }

    public bool Any =>
        WobblyEnabled || OpenKind is not null || CloseKind is not null || MinimizeKind is not null
        || SwitcherEnabled || HighlightEnabled || SlideBackEnabled || StretchEnabled
        || NotificationsEnabled || Dim is not null || SlideEnabled;

    public IPixelShader? FireShaderHandle { get; set; }

    public bool SlideEnabled { get; set; }

    public void SlideWorkspaces(SceneTree outgoing, SceneTree incoming, in Box area, int direction, Action done)
    {
        _slideDone = done;
        _slide.Begin(_runner.StackFor(outgoing), _runner.StackFor(incoming), area, direction);
    }

    public void DragWorkspaces(SceneTree outgoing, SceneTree? incoming, in Box area, int direction)
    {
        _slideDone = null;
        _slide.BeginInteractive(_runner.StackFor(outgoing), incoming is null ? null : _runner.StackFor(incoming), area, direction);
    }

    public double SlideProgress
    {
        get => _slide.Progress;
        set => _slide.Progress = value;
    }

    public bool SlideDragging => _slide.IsInteractive;

    public void SettleSlide(bool commit, Action done)
    {
        _slideDone = done;
        _slide.Settle(commit);
    }

    public bool SwitcherActive => _switcher.IsActive && !_switcher.IsDismissing;

    public int SwitcherSelected => _switcher.Selected;

    public void SwitcherBegin(IReadOnlyList<SceneTree> trees, in Box area, int selected)
    {
        _switcherStacks.Clear();
        foreach (var tree in trees)
        {
            _switcherStacks.Add(_runner.StackFor(tree));
        }

        if (_switcherStacks.Count > 0)
        {
            _switcher.Begin(_switcherStacks, area, selected);
        }
    }

    public void SwitcherSelect(int index) => _switcher.Select(index);

    public void SwitcherEnd()
    {
        _switcher.End();
        _switcherStacks.Clear();
    }

    public void OnClosing(object owner, SceneTree? source, SceneTree parent, IDisposable? attachment = null, string? closeKind = null)
    {
        closeKind ??= CloseKind;
        if (closeKind is null)
        {
            attachment?.Dispose();
            return;
        }

        _runner.BeginClose(
            source,
            parent,
            (_, stack) => BeginHide(stack, closeKind),
            owner,
            attachment);
    }

    public void CancelClosing(object owner) => _runner.CancelClose(owner);

    public void OnMoveGrab(SceneTree? tree, double localX, double localY, bool? wobblyOverride = null)
    {
        if (!(wobblyOverride ?? WobblyEnabled) || tree is null || tree.IsDestroyed)
        {
            return;
        }

        if (!_wobblies.TryGetValue(tree, out var entry))
        {
            var stack = _runner.StackFor(tree);
            var wobbly = new WobblyEffect();
            wobbly.Attach(stack);
            entry = (stack, wobbly);
            _wobblies[tree] = entry;
        }

        entry.Wobbly.Grab(localX, localY);
        _grabbed = entry.Wobbly;
    }

    public void OnMoved(int dx, int dy) => _grabbed?.NotifyMoved(dx, dy);

    public void OnGrabEnd()
    {
        _grabbed?.Release();
        _grabbed = null;
    }

    public void OnMapped(SceneTree? tree, string? openKind = null)
    {
        openKind ??= OpenKind;
        if (openKind is null)
        {
            return;
        }

        _runner.BeginOpen(tree, stack => BeginShow(stack, openKind));
    }

    public bool OnMinimize(
        SceneTree? tree, in Box window, in Box icon, bool restoring, double cursorX, double cursorY, Action? done = null)
    {
        if (MinimizeKind is null || tree is null || tree.IsDestroyed)
        {
            return false;
        }

        var stack = _runner.StackFor(tree);
        if (_minimizing.Remove(tree, out var previous))
        {
            previous.End(stack);
        }

        if (MinimizeKind == "squash")
        {
            var squash = new SquashEffect();
            var target = icon.IsEmpty ? FallbackIcon(window, cursorX, cursorY) : icon;
            if (!squash.Begin(stack, window, target, restoring, Minimize))
            {
                return false;
            }

            _minimizing[tree] = new MinimizeRun(Squash: squash, Done: done);
            return true;
        }

        var (lampIcon, edge) = icon.IsEmpty
            ? MagicLampEffect.FallbackTarget(window, cursorX, cursorY)
            : (icon, EdgeFor(window, icon));
        var lamp = new MagicLampEffect();
        if (!lamp.Begin(stack, window, lampIcon, edge, restoring, Minimize))
        {
            return false;
        }

        _minimizing[tree] = new MinimizeRun(Lamp: lamp, Done: done);
        return true;
    }

    public void SetHighlight(SceneTree? tree, bool highlighted)
    {
        if (!HighlightEnabled || tree is null || tree.IsDestroyed)
        {
            return;
        }

        if (!_highlighted.Contains(tree))
        {
            _highlighted.Add(tree);
        }

        _highlight.Highlight(_runner.StackFor(tree), highlighted, _lastTick, Highlight);
    }

    public void ClearHighlights()
    {
        foreach (var tree in _highlighted)
        {
            if (!tree.IsDestroyed)
            {
                _highlight.Clear(_runner.StackFor(tree));
            }
        }

        _highlighted.Clear();
    }

    public void OnRaised(SceneTree? tree, double fromX, double fromY)
    {
        if (!SlideBackEnabled || tree is null || tree.IsDestroyed)
        {
            return;
        }

        _slideBack.Move(_runner.StackFor(tree), fromX, fromY, 0, 0, durationFactor: 1.0);
    }

    public void OnNotificationMapped(SceneTree? tree, double fromX, double fromY)
    {
        if (!NotificationsEnabled || tree is null || tree.IsDestroyed)
        {
            return;
        }

        _ = _notifications.Slide(
            _runner.StackFor(tree), fromX, fromY, 0, 0, durationFactor: 1.0, removeWhenSettled: true);
    }

    public void OnResizeStart(SceneTree? tree, in Box frame, in Box from, in Box current, int originX, int originY)
    {
        if (!StretchEnabled || tree is null || tree.IsDestroyed)
        {
            return;
        }

        var stack = _runner.StackFor(tree);
        if (!_stretches.TryGetValue(tree, out var run))
        {
            run = new StretchRun(new StretchEffect());
            _stretches[tree] = run;
        }

        run.Frame = frame;
        run.OriginX = originX;
        run.OriginY = originY;
        _ = run.Effect.Capture(stack, frame, from, current, originX, originY, OpenClose);
    }

    public void OnResized(SceneTree? tree, in Box frame, in Box from, in Box to, int originX, int originY)
    {
        if (!StretchEnabled || tree is null || tree.IsDestroyed
            || !_stretches.TryGetValue(tree, out var run))
        {
            return;
        }

        run.Frame = frame;
        run.OriginX = originX;
        run.OriginY = originY;
        _ = run.Effect.Begin(_runner.StackFor(tree), frame, from, to, originX, originY, OpenClose);
    }

    public void Forget(SceneTree tree)
    {
        _runner.Forget(tree);
        _wobblies.Remove(tree);
        _minimizing.Remove(tree);
        _highlighted.Remove(tree);
        _stretches.Remove(tree);
    }

    public void FadeDim(bool inactive)
    {
        Dim?.FadeTo(inactive ? 1.0 : 0.0, _lastTick, DimFade);
        DimChanged?.Invoke(Dim?.Dim ?? 1.0);
    }

    public bool Step(in FrameTick tick)
    {
        if (tick.TargetPresentNanos <= _lastTick.TargetPresentNanos)
        {
            return _lastMoving || Running;
        }

        _lastTick = tick;
        var moving = false;
        foreach (var (tree, entry) in _wobblies)
        {
            if (!tree.IsDestroyed)
            {
                moving |= entry.Wobbly.Step(tick);
            }
        }

        _runner.Step(tick);

        if (_switcher.IsActive)
        {
            _ = _switcher.Step(tick);
        }

        if (_slide.IsAnimating && !_slide.Step(tick))
        {
            var done = _slideDone;
            _slideDone = null;
            done?.Invoke();
        }

        StepMinimizing(tick);
        StepHighlights(tick);
        StepStretches(tick);

        moving |= _slideBack.Step(tick);
        moving |= _notifications.Step(tick);

        if (Dim is { IsAnimating: true } dim)
        {
            _ = dim.Step(tick);
            DimChanged?.Invoke(dim.Dim);
            moving = true;
        }

        PruneDead();
        _lastMoving = moving;
        return moving || Running;
    }

    public void Dispose()
    {
        _runner.Dispose();
        _minimizing.Clear();
        _highlighted.Clear();
        _stretches.Clear();
        _wobblies.Clear();
    }

    private bool _lastMoving;

    private bool Running =>
        _slide.IsAnimating || _switcher.IsActive || _runner.IsRunning
        || _minimizing.Count > 0 || _stretches.Count > 0
        || _slideBack.IsActive || _notifications.IsActive || _highlight.IsActive;

    private static Box FallbackIcon(in Box window, double cursorX, double cursorY)
    {
        var (icon, _) = MagicLampEffect.FallbackTarget(window, cursorX, cursorY);
        return icon;
    }

    private static MinimizeEdge EdgeFor(in Box window, in Box icon)
    {
        var dx = (icon.X + (icon.Width / 2.0)) - (window.X + (window.Width / 2.0));
        var dy = (icon.Y + (icon.Height / 2.0)) - (window.Y + (window.Height / 2.0));
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx < 0 ? MinimizeEdge.Left : MinimizeEdge.Right;
        }

        return dy < 0 ? MinimizeEdge.Top : MinimizeEdge.Bottom;
    }

    private AnimationStep? BeginShow(TransformStack stack, string kind)
    {
        switch (kind)
        {
            case "glide":
            {
                var glide = new GlideEffect();
                return glide.Begin(stack, hiding: false, Glide) ? glide.Step : null;
            }

            case "sheet":
            {
                var sheet = new SheetEffect();
                return sheet.Begin(stack, hiding: false, parentDrop: 0, Sheet) ? sheet.Step : null;
            }

            case "fade" or "zoom":
            {
                var animation = new OpenCloseAnimation(kind == "zoom" ? OpenCloseKind.Zoom : OpenCloseKind.Fade);
                return animation.Begin(stack, hiding: false, OpenClose) ? animation.Step : null;
            }

            default:
                return null;
        }
    }

    private AnimationStep? BeginHide(TransformStack stack, string kind)
    {
        switch (kind)
        {
            case "fire" or "fire-gpu":
            {
                var fire = new FireEffect { Shader = FireShaderHandle };
                fire.Begin(stack, hiding: true, Fire.Nanos);
                return fire.Step;
            }

            case "glide":
            {
                var glide = new GlideEffect();
                return glide.Begin(stack, hiding: true, Glide) ? glide.Step : null;
            }

            case "sheet":
            {
                var sheet = new SheetEffect();
                return sheet.Begin(stack, hiding: true, parentDrop: 0, Sheet) ? sheet.Step : null;
            }

            case "fall-apart":
            {
                var apart = new FallApartEffect();
                return apart.Begin(stack, FallApart)
                    ? (TransformStack _, in FrameTick tick) => apart.Step(tick)
                    : null;
            }

            case "fade" or "zoom":
            {
                var animation = new OpenCloseAnimation(kind == "zoom" ? OpenCloseKind.Zoom : OpenCloseKind.Fade);
                return animation.Begin(stack, hiding: true, OpenClose) ? animation.Step : null;
            }

            default:
                return null;
        }
    }

    private void StepMinimizing(in FrameTick tick)
    {
        if (_minimizing.Count == 0)
        {
            return;
        }

        List<SceneTree>? done = null;
        foreach (var (tree, run) in _minimizing)
        {
            if (tree.IsDestroyed || !run.Step(_runner.StackFor(tree), tick))
            {
                (done ??= []).Add(tree);
            }
        }

        if (done is null)
        {
            return;
        }

        foreach (var tree in done)
        {
            if (!_minimizing.Remove(tree, out var run))
            {
                continue;
            }

            if (!tree.IsDestroyed)
            {
                run.End(_runner.StackFor(tree));
            }

            run.Done?.Invoke();
        }
    }

    private void StepHighlights(in FrameTick tick) => _ = _highlight.Step(tick);

    private void StepStretches(in FrameTick tick)
    {
        if (_stretches.Count == 0)
        {
            return;
        }

        List<SceneTree>? done = null;
        foreach (var (tree, run) in _stretches)
        {
            if (tree.IsDestroyed)
            {
                (done ??= []).Add(tree);
                continue;
            }

            var stack = _runner.StackFor(tree);
            if (!run.Effect.Step(stack, run.Frame, run.OriginX, run.OriginY, tick))
            {
                run.Effect.End(stack);
                (done ??= []).Add(tree);
            }
        }

        if (done is null)
        {
            return;
        }

        foreach (var tree in done)
        {
            _stretches.Remove(tree);
        }
    }

    private void PruneDead()
    {
        List<SceneTree>? dead = null;
        foreach (var tree in _wobblies.Keys)
        {
            if (tree.IsDestroyed)
            {
                (dead ??= []).Add(tree);
            }
        }

        if (dead is not null)
        {
            foreach (var tree in dead)
            {
                _wobblies.Remove(tree);
            }
        }
    }

    private sealed class StretchRun(StretchEffect effect)
    {
        public StretchEffect Effect { get; } = effect;

        public Box Frame { get; set; }

        public int OriginX { get; set; }

        public int OriginY { get; set; }
    }

    private readonly record struct MinimizeRun(MagicLampEffect? Lamp = null, SquashEffect? Squash = null, Action? Done = null)
    {
        public bool Step(TransformStack stack, in FrameTick tick) =>
            Lamp is { } lamp ? lamp.Step(tick) : Squash?.Step(stack, tick) == true;

        public void End(TransformStack stack)
        {
            Lamp?.End(stack);
            Squash?.End(stack);
        }
    }
}
