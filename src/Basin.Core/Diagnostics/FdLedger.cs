using System.Diagnostics;

namespace Basin.Diagnostics;

public sealed class FdLedger
{
    private static readonly HashSet<int> _clientOwned = [];

    private readonly Dictionary<int, string> _open = [];

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

    [Conditional("DEBUG")]
    public void Acquired(int fd, string tag)
    {
        lock (_open)
        {
            if (!_open.TryAdd(fd, tag))
            {
                throw new InvalidOperationException($"fd {fd} acquired twice ('{_open[fd]}', now '{tag}').");
            }
        }
    }

    [Conditional("DEBUG")]
    public void AcquiredFromClient(int fd, string tag)
    {
        Acquired(fd, tag);
        lock (_clientOwned)
        {
            _clientOwned.Add(fd);
        }
    }

    [Conditional("DEBUG")]
    public static void AssertNotClientOwned(int fd)
    {
        lock (_clientOwned)
        {
            if (_clientOwned.Contains(fd))
            {
                throw new InvalidOperationException(
                    $"fd {fd} came from a client and must be released through WlClient.CloseFd, not close(2).");
            }
        }
    }

    [Conditional("DEBUG")]
    public void Transferred(int fd) => Forget(fd, "transferred");

    [Conditional("DEBUG")]
    public void Closed(int fd) => Forget(fd, "closed");

    [Conditional("DEBUG")]
    public void AssertEmpty()
    {
        lock (_open)
        {
            if (_open.Count > 0)
            {
                var leaks = string.Join(", ", _open.Select(p => $"{p.Key} ({p.Value})"));
                throw new InvalidOperationException($"fd ledger not balanced: {leaks}");
            }
        }
    }

    private void Forget(int fd, string how)
    {
        lock (_open)
        {
            if (!_open.Remove(fd))
            {
                throw new InvalidOperationException($"fd {fd} {how} but was never acquired.");
            }
        }

        lock (_clientOwned)
        {
            _clientOwned.Remove(fd);
        }
    }
}
