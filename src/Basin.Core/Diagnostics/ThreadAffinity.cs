using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// Records the thread that owns an object, so that debug builds can catch calls from other threads.
/// </summary>
public readonly struct ThreadAffinity
{
    [ThreadStatic]
    private static int _actingAs;

    private readonly int _threadId;

    private ThreadAffinity(int threadId) => _threadId = threadId;

    /// <summary>
    /// Records the current thread, or the thread that the current thread acts as, as the owner.
    /// </summary>
    /// <returns>The affinity of the current thread.</returns>
    public static ThreadAffinity Capture() => new(EffectiveThreadId);

    /// <summary>
    /// Makes the current thread act as the owner thread until the scope is disposed. A worker that the owner waits on can use this.
    /// </summary>
    /// <returns>The scope that restores the previous identity.</returns>
    public Scope Adopt()
    {
        var previous = _actingAs;
        _actingAs = _threadId;
        return new(previous);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the caller is not the owner thread. The call is removed from builds without <c>DEBUG</c>.
    /// </summary>
    [Conditional("DEBUG")]
    public void Assert()
    {
        if (_threadId != EffectiveThreadId)
        {
            throw new InvalidOperationException(
                $"Cross-thread access: object owned by thread {_threadId}, called from {Environment.CurrentManagedThreadId}.");
        }
    }

    private static int EffectiveThreadId => _actingAs != 0 ? _actingAs : Environment.CurrentManagedThreadId;

    /// <summary>
    /// Restores the thread identity that was active before <see cref="Adopt"/>.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly int _previous;

        internal Scope(int previous) => _previous = previous;

        /// <summary>
        /// Restores the previous thread identity.
        /// </summary>
        public void Dispose() => _actingAs = _previous;
    }
}
