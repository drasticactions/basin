using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Sets the soft limit on open files of the compositor process, and restores the original limit around a child spawn.
/// </summary>
public static class FileDescriptorLimit
{
    private const int ResourceOpenFiles = 7;

    private const ulong DesiredOpenFiles = 4096;

    private static Limit? _original;

    [StructLayout(LayoutKind.Sequential)]
    private struct Limit
    {
        public ulong Current;

        public ulong Maximum;
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Raise"/> changed the limit. It is false when the soft limit was already high enough.
    /// </summary>
    public static bool IsRaised => _original is not null;

    /// <summary>
    /// Sets the soft limit on open files to 4096, or to the hard limit when that is lower. A soft limit that is already higher stays. A failure is logged and the system limit stays.
    /// </summary>
    /// <param name="log">The logger for the outcome.</param>
    public static void Raise(BasinLogger log)
    {
        if (getrlimit(ResourceOpenFiles, out var original) != 0)
        {
            log.Error($"getrlimit failed, using the system default file descriptor limit");
            return;
        }

        var raised = Raised(original);
        if (raised.Current == original.Current)
        {
            log.Debug($"the file descriptor limit of the compositor process stays at {original.Current}");
            return;
        }

        _original = original;
        if (setrlimit(ResourceOpenFiles, in raised) != 0)
        {
            log.Error($"setrlimit failed, using the system default file descriptor limit of {original.Current}");
            _original = null;
            return;
        }

        log.Info($"raised the file descriptor limit of the compositor process to {raised.Current}");
    }

    /// <summary>
    /// Restores the original soft limit, so that a child does not inherit the higher limit.
    /// </summary>
    /// <returns><see langword="true"/> when the original limit was restored. It is false when <see cref="Raise"/> did not change the limit.</returns>
    public static bool LowerForSpawn() =>
        _original is { } original && setrlimit(ResourceOpenFiles, in original) == 0;

    /// <summary>
    /// Sets the soft limit again after <see cref="LowerForSpawn"/>. It does nothing when <see cref="Raise"/> did not change the limit.
    /// </summary>
    /// <param name="log">The logger for a failure.</param>
    public static void RaiseAfterSpawn(BasinLogger log)
    {
        if (_original is not { } original)
        {
            return;
        }

        var raised = Raised(original);
        if (setrlimit(ResourceOpenFiles, in raised) != 0)
        {
            log.Error($"failed to raise the file descriptor limit of the compositor process again");
        }
    }

    internal static ulong SoftLimitFor(ulong current, ulong hard) =>
        Math.Max(current, Math.Min(DesiredOpenFiles, hard));

    private static Limit Raised(Limit original) =>
        new() { Current = SoftLimitFor(original.Current, original.Maximum), Maximum = original.Maximum };

    [DllImport("libc", SetLastError = true)]
    private static extern int getrlimit(int resource, out Limit limit);

    [DllImport("libc", SetLastError = true)]
    private static extern int setrlimit(int resource, in Limit limit);
}
