using System.Diagnostics;

namespace Basin.WindowManager;

/// <summary>
/// The check that window-manager state stays on the thread that created the <see cref="RiverWindowManager"/>.
/// </summary>
public static class WmThreadAffinity
{
    private static int _owner;

    /// <summary>
    /// Makes the calling thread the owner. The <see cref="RiverWindowManager"/> constructor calls it. The owner is process-wide.
    /// </summary>
    public static void Claim() => _owner = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a thread other than the owner calls it. It does nothing before a claim, and the call compiles away outside debug builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Assert()
    {
        var owner = _owner;
        if (owner != 0 && owner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                $"window manager state touched from thread {Environment.CurrentManagedThreadId}; it belongs to thread {owner}");
        }
    }
}
