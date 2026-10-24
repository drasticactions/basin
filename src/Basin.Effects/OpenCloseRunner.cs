using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Runs the open and close animations of many windows. A close animates a snapshot of the window, so the window itself can go at once.
/// </summary>
public sealed class OpenCloseRunner : IDisposable
{
    private readonly Dictionary<SceneTree, TransformStack> _stacks = [];
    private readonly List<Opening> _openings = [];
    private readonly List<Closing> _closings = [];
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether an open or a close animation runs.
    /// </summary>
    public bool IsRunning => _openings.Count > 0 || _closings.Count > 0;

    /// <summary>
    /// Gets the number of running open animations.
    /// </summary>
    public int OpeningCount => _openings.Count;

    /// <summary>
    /// Gets the number of running close animations.
    /// </summary>
    public int ClosingCount => _closings.Count;

    /// <summary>
    /// Gets the transform stack that the runner keeps for a window tree, and creates it on the first call.
    /// </summary>
    /// <param name="tree">The window tree.</param>
    /// <returns>The same stack for the same tree until <see cref="Forget"/> or the tree is destroyed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Starts an open animation on a live window tree.
    /// </summary>
    /// <param name="tree">The window tree. A <see langword="null"/> or destroyed tree starts nothing.</param>
    /// <param name="begin">A function that starts the animation on the stack and returns its step, or <see langword="null"/> to start nothing.</param>
    /// <returns><see langword="true"/> when an animation started.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="begin"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Captures a snapshot of a window tree under a parent and starts a close animation on it. The runner destroys the snapshot when the animation ends.
    /// </summary>
    /// <param name="source">The window tree to capture. A <see langword="null"/> or destroyed tree starts nothing.</param>
    /// <param name="parent">The tree that receives the snapshot.</param>
    /// <param name="begin">A function that starts the animation on the snapshot and returns its step, or <see langword="null"/> to start nothing.</param>
    /// <param name="owner">A key for <see cref="CancelClose"/>. A new close with the same key cancels the earlier one.</param>
    /// <param name="attachment">An object that the runner disposes with the snapshot. It is also disposed when nothing starts.</param>
    /// <returns><see langword="true"/> when an animation started.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="begin"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Stops every close animation with a key, and destroys its snapshot and attachment.
    /// </summary>
    /// <param name="owner">The key that <see cref="BeginClose"/> received.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Drops the stack and the open animations of a window tree. It does not remove the nodes that the animations added.
    /// </summary>
    /// <param name="tree">The window tree.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Steps every animation, and removes the ones that ended or whose tree is destroyed.
    /// </summary>
    /// <param name="tick">The frame tick.</param>
    /// <returns><see langword="true"/> while an animation still runs. It is <see langword="false"/> after <see cref="Dispose"/>.</returns>
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

    /// <summary>
    /// Destroys the snapshot and the attachment of every running close, and drops every open. A second call does nothing.
    /// </summary>
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
