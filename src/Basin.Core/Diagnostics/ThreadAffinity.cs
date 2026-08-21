using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public readonly struct ThreadAffinity
{
    [ThreadStatic]
    private static int _actingAs;

    private readonly int _threadId;

    private ThreadAffinity(int threadId) => _threadId = threadId;

    public static ThreadAffinity Capture() => new(EffectiveThreadId);

    public Scope Adopt()
    {
        var previous = _actingAs;
        _actingAs = _threadId;
        return new(previous);
    }

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

    public readonly struct Scope : IDisposable
    {
        private readonly int _previous;

        internal Scope(int previous) => _previous = previous;

        public void Dispose() => _actingAs = _previous;
    }
}
