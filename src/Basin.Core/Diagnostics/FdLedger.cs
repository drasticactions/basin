using System.Diagnostics;

namespace Basin.Diagnostics;

/// <summary>
/// Records the file descriptors that an owner holds, so that teardown can prove that it closed them. The recording calls exist only in <c>DEBUG</c> builds.
/// </summary>
public sealed class FdLedger
{
    private static readonly HashSet<int> _clientOwned = [];

    private readonly Dictionary<int, string> _open = [];

    /// <summary>
    /// Gets the number of descriptors that are recorded as open.
    /// </summary>
    public int OpenCount
    {
        get
        {
            lock (_open)
            {
                return _open.Count;
            }
        }
    }

    /// <summary>
    /// Records that a descriptor is now open. It throws <see cref="InvalidOperationException"/> when the descriptor is already recorded.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    /// <param name="tag">A label that names the descriptor in a failure message.</param>
    [Conditional("DEBUG")]
    public void Acquired(int fd, string tag)
    {
        lock (_open)
        {
            if (!_open.TryAdd(fd, tag))
            {
                throw new InvalidOperationException($"The fd {fd} was acquired twice, first as '{_open[fd]}' and now as '{tag}'.");
            }
        }
    }

    /// <summary>
    /// Records a descriptor that a Wayland client sent. Such a descriptor must be closed through libwayland, not <c>close(2)</c>.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    /// <param name="tag">A label that names the descriptor in a failure message.</param>
    [Conditional("DEBUG")]
    public void AcquiredFromClient(int fd, string tag)
    {
        Acquired(fd, tag);
        lock (_clientOwned)
        {
            _clientOwned.Add(fd);
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a descriptor came from a client through <see cref="AcquiredFromClient"/>. Call it before <c>close(2)</c>.
    /// </summary>
    /// <param name="fd">The descriptor that is about to close.</param>
    [Conditional("DEBUG")]
    public static void AssertNotClientOwned(int fd)
    {
        lock (_clientOwned)
        {
            if (_clientOwned.Contains(fd))
            {
                throw new InvalidOperationException(
                    $"The fd {fd} came from a client. Release it through WlClient.CloseFd, not close(2).");
            }
        }
    }

    /// <summary>
    /// Records that the ownership of a descriptor moved to another object. It throws when the descriptor is not recorded.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    [Conditional("DEBUG")]
    public void Transferred(int fd) => Forget(fd, "transferred");

    /// <summary>
    /// Records that a descriptor is closed. It throws when the descriptor is not recorded.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    [Conditional("DEBUG")]
    public void Closed(int fd) => Forget(fd, "closed");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> that lists each descriptor that is still recorded as open.
    /// </summary>
    [Conditional("DEBUG")]
    public void AssertEmpty()
    {
        lock (_open)
        {
            if (_open.Count > 0)
            {
                var leaks = string.Join(", ", _open.Select(p => $"{p.Key} ({p.Value})"));
                throw new InvalidOperationException($"The fd ledger is not balanced. These fds are still open: {leaks}.");
            }
        }
    }

    private void Forget(int fd, string how)
    {
        lock (_open)
        {
            if (!_open.Remove(fd))
            {
                throw new InvalidOperationException($"The fd {fd} was {how}, but it was never acquired.");
            }
        }

        lock (_clientOwned)
        {
            _clientOwned.Remove(fd);
        }
    }
}
