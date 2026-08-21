using System.Diagnostics;

namespace Basin.WindowManager;

public static class WmThreadAffinity
{
    private static int _owner;

    public static void Claim() => _owner = Environment.CurrentManagedThreadId;

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
