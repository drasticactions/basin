using Basin;
using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class EffectsPolicy
{
    private readonly bool _wobblyEnabled;
    private readonly OpenCloseKind? _openKind;
    private readonly string? _closeKind;
    private readonly bool _switcherEnabled;
    private readonly List<(object Owner, SceneSnapshot Snapshot, TransformStack Stack, OpenCloseAnimation? Animation, FireEffect? Fire, IDisposable? Attachment)> _closings = [];
    private readonly Dictionary<SceneTree, TransformStack> _stacks = [];
    private readonly Dictionary<SceneTree, (TransformStack Stack, WobblyEffect Wobbly)> _wobblies = [];
    private readonly List<(SceneTree Tree, TransformStack Stack, OpenCloseAnimation Animation)> _openings = [];
    private readonly SwitcherEffect _switcher = new();
    private readonly List<TransformStack> _switcherStacks = [];
    private readonly SlideTransition _slide = new();
    private Action? _slideDone;
    private WobblyEffect? _grabbed;
    private FrameTick _lastTick;

    public EffectsPolicy(bool wobbly, string? openAnimation, string? closeAnimation, bool switcher)
    {
        _wobblyEnabled = wobbly;
        _openKind = openAnimation switch
        {
            "fade" => OpenCloseKind.Fade,
            "zoom" => OpenCloseKind.Zoom,
            _ => null,
        };
        _closeKind = closeAnimation;
        _switcherEnabled = switcher;
    }

    public bool Any => _wobblyEnabled || _openKind is not null || _closeKind is not null || _switcherEnabled || SlideEnabled;

    public IPixelShader? FireShaderHandle { get; set; }

    public bool SlideEnabled { get; set; }

    public void SlideWorkspaces(SceneTree outgoing, SceneTree incoming, in Box area, int direction, Action done)
    {
        _slideDone = done;
        _slide.Begin(StackFor(outgoing), StackFor(incoming), area, direction);
    }

    public void DragWorkspaces(SceneTree outgoing, SceneTree? incoming, in Box area, int direction)
    {
        _slideDone = null;
        _slide.BeginInteractive(StackFor(outgoing), incoming is null ? null : StackFor(incoming), area, direction);
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

    public bool SwitcherEnabled => _switcherEnabled;

    public bool SwitcherActive => _switcher.IsActive && !_switcher.IsDismissing;

    public int SwitcherSelected => _switcher.Selected;

    public void SwitcherBegin(IReadOnlyList<SceneTree> trees, in Box area, int selected)
    {
        _switcherStacks.Clear();
        foreach (var tree in trees)
        {
            _switcherStacks.Add(StackFor(tree));
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

    private TransformStack StackFor(SceneTree tree)
    {
        if (!_stacks.TryGetValue(tree, out var stack))
        {
            stack = new TransformStack(tree);
            _stacks[tree] = stack;
        }

        return stack;
    }

    public void OnClosing(object owner, SceneTree? source, SceneTree parent, IDisposable? attachment = null)
    {
        if (_closeKind is null || source is null || source.IsDestroyed)
        {
            attachment?.Dispose();
            return;
        }

        CancelClosing(owner);
        var snapshot = SceneSnapshot.Capture(source, parent);
        var stack = new TransformStack(snapshot.Tree);
        if (_closeKind is "fire" or "fire-gpu")
        {
            var fire = new FireEffect { Shader = FireShaderHandle };
            fire.Begin(stack, hiding: true, 450_000_000);
            _closings.Add((owner, snapshot, stack, null, fire, attachment));
        }
        else
        {
            var animation = new OpenCloseAnimation(_closeKind == "zoom" ? OpenCloseKind.Zoom : OpenCloseKind.Fade);
            animation.Begin(stack, hiding: true, 250_000_000);
            _closings.Add((owner, snapshot, stack, animation, null, attachment));
        }
    }

    public void CancelClosing(object owner)
    {
        for (var i = _closings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_closings[i].Owner, owner))
            {
                _closings[i].Snapshot.Destroy();
                _closings[i].Attachment?.Dispose();
                _closings.RemoveAt(i);
            }
        }
    }

    public void OnMoveGrab(SceneTree? tree, double localX, double localY)
    {
        if (!_wobblyEnabled || tree is null || tree.IsDestroyed)
        {
            return;
        }

        if (!_wobblies.TryGetValue(tree, out var entry))
        {
            var stack = StackFor(tree);
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

    public void OnMapped(SceneTree? tree)
    {
        if (_openKind is not { } kind || tree is null || tree.IsDestroyed)
        {
            return;
        }

        var stack = StackFor(tree);
        var animation = new OpenCloseAnimation(kind);
        animation.Begin(stack, hiding: false, 250_000_000);
        _openings.Add((tree, stack, animation));
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

        for (var i = _openings.Count - 1; i >= 0; i--)
        {
            var (tree, stack, animation) = _openings[i];
            if (tree.IsDestroyed || !animation.Step(stack, tick))
            {
                _openings.RemoveAt(i);
            }
        }

        for (var i = _closings.Count - 1; i >= 0; i--)
        {
            var (_, snapshot, stack, animation, fire, attachment) = _closings[i];
            var running = fire is not null ? fire.Step(stack, tick) : animation!.Step(stack, tick);
            if (!running)
            {
                snapshot.Destroy();
                attachment?.Dispose();
                _closings.RemoveAt(i);
            }
        }

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

        PruneDead();
        _lastMoving = moving;
        return moving || Running;
    }

    private bool _lastMoving;

    private bool Running => _slide.IsAnimating || _switcher.IsActive || _openings.Count > 0 || _closings.Count > 0;

    private void PruneDead()
    {
        List<SceneTree>? dead = null;
        foreach (var tree in _stacks.Keys)
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
                _stacks.Remove(tree);
                _wobblies.Remove(tree);
            }
        }
    }
}
