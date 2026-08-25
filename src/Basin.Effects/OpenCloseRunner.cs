using Basin.Scene;

namespace Basin.Effects;

public sealed class OpenCloseRunner : IDisposable
{
    private readonly Dictionary<SceneTree, TransformStack> _stacks = [];
    private readonly List<Opening> _openings = [];
    private readonly List<Closing> _closings = [];
    private bool _disposed;

    public bool IsRunning => _openings.Count > 0 || _closings.Count > 0;

    public int OpeningCount => _openings.Count;

    public int ClosingCount => _closings.Count;

    public TransformStack StackFor(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (!_stacks.TryGetValue(tree, out var stack))
        {
            stack = new TransformStack(tree);
            _stacks[tree] = stack;
        }

        return stack;
    }

    public bool BeginOpen(SceneTree? tree, Func<TransformStack, AnimationStep?> begin)
    {
        ArgumentNullException.ThrowIfNull(begin);
        if (_disposed || tree is null || tree.IsDestroyed)
        {
            return false;
        }

        var stack = StackFor(tree);
        if (begin(stack) is not { } step)
        {
            return false;
        }

        _openings.Add(new Opening(tree, stack, step));
        return true;
    }

    public bool BeginClose(
        SceneTree? source,
        SceneTree parent,
        Func<SceneSnapshot, TransformStack, AnimationStep?> begin,
        object? owner = null,
        IDisposable? attachment = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(begin);
        if (_disposed || source is null || source.IsDestroyed)
        {
            attachment?.Dispose();
            return false;
        }

        if (owner is not null)
        {
            CancelClose(owner);
        }

        var snapshot = SceneSnapshot.Capture(source, parent);
        var stack = new TransformStack(snapshot.Tree);
        if (begin(snapshot, stack) is not { } step)
        {
            snapshot.Destroy();
            attachment?.Dispose();
            return false;
        }

        _closings.Add(new Closing(snapshot, stack, step, owner, attachment));
        return true;
    }

    public void CancelClose(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        for (var i = _closings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_closings[i].Owner, owner))
            {
                _closings[i].Release();
                _closings.RemoveAt(i);
            }
        }
    }

    public void Forget(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _stacks.Remove(tree);
        for (var i = _openings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_openings[i].Tree, tree))
            {
                _openings.RemoveAt(i);
            }
        }
    }

    public bool Step(in FrameTick tick)
    {
        if (_disposed)
        {
            return false;
        }

        for (var i = _openings.Count - 1; i >= 0; i--)
        {
            var opening = _openings[i];
            if (opening.Tree.IsDestroyed || !opening.Step(opening.Stack, tick))
            {
                _openings.RemoveAt(i);
            }
        }

        for (var i = _closings.Count - 1; i >= 0; i--)
        {
            var closing = _closings[i];
            if (!closing.Step(closing.Stack, tick))
            {
                closing.Release();
                _closings.RemoveAt(i);
            }
        }

        Prune();
        return IsRunning;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var closing in _closings)
        {
            closing.Release();
        }

        _closings.Clear();
        _openings.Clear();
        _stacks.Clear();
    }

    private void Prune()
    {
        List<SceneTree>? dead = null;
        foreach (var tree in _stacks.Keys)
        {
            if (tree.IsDestroyed)
            {
                (dead ??= []).Add(tree);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var tree in dead)
        {
            _stacks.Remove(tree);
        }
    }

    private sealed record Opening(SceneTree Tree, TransformStack Stack, AnimationStep Step);

    private sealed record Closing(
        SceneSnapshot Snapshot, TransformStack Stack, AnimationStep Step, object? Owner, IDisposable? Attachment)
    {
        public void Release()
        {
            Snapshot.Destroy();
            Attachment?.Dispose();
        }
    }
}
