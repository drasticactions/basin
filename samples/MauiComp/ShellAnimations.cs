using Basin;
using Basin.Effects;
using Basin.Scene;

namespace MauiComp;

internal sealed class ShellAnimations : IDisposable
{
    private static readonly EasingCurve FadeCurve = EasingCurve.OutCubic;

    private static readonly AnimationDuration FadeDuration = new(150);

    private static readonly AnimationDuration MinimizeDuration = new(250);

    private readonly OpenCloseRunner _runner = new();
    private readonly Dictionary<SceneTree, (SquashEffect Effect, Action? Done)> _minimizing = [];
    private bool _disposed;

    public Action? Changed { get; set; }

    public bool IsRunning => _runner.IsRunning || _minimizing.Count > 0;

    public bool BeginMinimize(ShellWindow window, in Box icon, Action done) =>
        BeginSquash(window, icon, restoring: false, done);

    public bool BeginRestore(ShellWindow window, in Box icon) =>
        BeginSquash(window, icon, restoring: true, null);

    public void BeginMap(ShellWindow window)
    {
        if (_disposed)
        {
            return;
        }

        _runner.BeginOpen(window.Tree, stack =>
        {
            var animation = new OpenCloseAnimation(OpenCloseKind.Fade, FadeCurve);
            animation.Begin(stack, hiding: false, FadeDuration.Nanos);
            return animation.Step;
        });
        Changed?.Invoke();
    }

    public void BeginUnmap(ShellWindow window)
    {
        if (_disposed || window.Tree.IsDestroyed || window.Tree.Parent is not { } parent || !window.Tree.Enabled)
        {
            return;
        }

        var x = window.Tree.X;
        var y = window.Tree.Y;
        _runner.BeginClose(window.Tree, parent, (snapshot, stack) =>
        {
            snapshot.Tree.SetPosition(x, y);
            var animation = new OpenCloseAnimation(OpenCloseKind.Fade, FadeCurve);
            animation.Begin(stack, hiding: true, FadeDuration.Nanos);
            return animation.Step;
        });
        Changed?.Invoke();
    }

    public void Step(in FrameTick tick)
    {
        if (_disposed)
        {
            return;
        }

        _runner.Step(tick);
        StepMinimizing(tick);
        if (IsRunning)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var (tree, run) in _minimizing)
        {
            if (!tree.IsDestroyed)
            {
                run.Effect.End(_runner.StackFor(tree));
            }
        }

        _minimizing.Clear();
        _runner.Dispose();
    }

    private bool BeginSquash(ShellWindow window, in Box icon, bool restoring, Action? done)
    {
        if (_disposed || window.Tree.IsDestroyed || icon.IsEmpty)
        {
            return false;
        }

        var tree = window.Tree;
        var stack = _runner.StackFor(tree);
        if (_minimizing.Remove(tree, out var previous))
        {
            previous.Effect.End(stack);
        }

        var geometry = window.Window.Xdg.EffectiveGeometry;
        var bounds = new Box(geometry.X, geometry.Y - ShellTitlebar.Height, geometry.Width, geometry.Height + ShellTitlebar.Height);
        var local = new Box(icon.X - tree.X, icon.Y - tree.Y, icon.Width, icon.Height);
        var effect = new SquashEffect();
        if (!effect.Begin(stack, bounds, local, restoring, MinimizeDuration))
        {
            return false;
        }

        _minimizing[tree] = (effect, done);
        Changed?.Invoke();
        return true;
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
            if (tree.IsDestroyed || !run.Effect.Step(_runner.StackFor(tree), tick))
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
                run.Effect.End(_runner.StackFor(tree));
            }

            run.Done?.Invoke();
        }
    }
}
