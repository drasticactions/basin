using System.Runtime.InteropServices;

namespace Basin.Freedesktop;

/// <summary>
/// An inotify watch on a set of directories, one level deep each. It reports each changed directory once per coalescing period.
/// The class is not thread-safe.
/// </summary>
public sealed partial class DirectoryWatch : IDisposable
{
    /// <summary>
    /// The delay in milliseconds from the first event to <see cref="Changed"/> when the watch runs on an event loop.
    /// </summary>
    public const int CoalesceMillis = 100;

    private const int NonBlock = 0x800;
    private const int CloseOnExec = 0x80000;
    private const uint Mask = 0x2 | 0x4 | 0x8 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400 | 0x800;

    private readonly ICompositorEventLoop? _loop;
    private readonly int _fd;
    private readonly IEventSource? _source;
    private readonly Dictionary<int, string> _watches = [];
    private readonly Dictionary<string, int> _paths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly List<string> _flushing = [];
    private IEventSource? _flush;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryWatch"/> class. Off Linux, or when inotify is not available, <see cref="Available"/> is false.
    /// </summary>
    /// <param name="loop">The loop that reads the descriptor and runs the coalescing timer, or <see langword="null"/> when the owner calls <see cref="Read"/> and <see cref="Flush"/>.</param>
    public DirectoryWatch(ICompositorEventLoop? loop = null)
    {
        _loop = loop;
        _fd = OperatingSystem.IsLinux() ? inotify_init1(NonBlock | CloseOnExec) : -1;
        if (_fd >= 0 && loop is not null)
        {
            _source = loop.AddFd(_fd, FdReadiness.Readable, OnReadable);
        }
    }

    /// <summary>
    /// Occurs once for each changed directory when <see cref="Flush"/> runs. The argument is the path given to <see cref="Add(string)"/>.
    /// </summary>
    public event Action<string>? Changed;

    /// <summary>
    /// Gets the non-blocking inotify descriptor that an owner without a loop polls, or -1 when <see cref="Available"/> is false. The watch owns it.
    /// </summary>
    public int Fd => _fd;

    /// <summary>
    /// Gets a value indicating whether inotify is available. When it is false, nothing is watched and <see cref="Changed"/> never occurs.
    /// </summary>
    public bool Available => _fd >= 0;

    /// <summary>
    /// Starts to watch one directory. Adding a path that is already watched does nothing.
    /// </summary>
    /// <param name="path">The directory to watch. Its subdirectories are not watched.</param>
    /// <returns><see langword="true"/> when the path is watched now, <see langword="false"/> when inotify is not available or refuses the path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public bool Add(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_fd < 0 || _paths.ContainsKey(path))
        {
            return _fd >= 0;
        }

        var wd = inotify_add_watch(_fd, path, Mask);
        if (wd < 0)
        {
            return false;
        }

        _watches[wd] = path;
        _paths[path] = wd;
        return true;
    }

    /// <summary>
    /// Stops watching a directory. A path that is not watched is ignored.
    /// </summary>
    /// <param name="path">The path given to <see cref="Add(string)"/>.</param>
    public void Remove(string path)
    {
        if (_paths.Remove(path, out var wd))
        {
            _watches.Remove(wd);
            _ = inotify_rm_watch(_fd, wd);
        }
    }

    /// <summary>
    /// Drains the descriptor and marks each watched directory that has events. It does not raise <see cref="Changed"/>.
    /// </summary>
    /// <returns><see langword="true"/> when at least one directory is marked and waits for <see cref="Flush"/>.</returns>
    public unsafe bool Read()
    {
        if (_fd < 0)
        {
            return false;
        }

        var buffer = stackalloc byte[4096];
        while (true)
        {
            var read = read_fd(_fd, buffer, 4096);
            if (read <= 0)
            {
                break;
            }

            var offset = 0;
            while (offset + 16 <= read)
            {
                var wd = *(int*)(buffer + offset);
                var length = *(uint*)(buffer + offset + 12);
                if (_watches.TryGetValue(wd, out var path))
                {
                    _dirty.Add(path);
                }

                offset += 16 + (int)length;
            }
        }

        return _dirty.Count > 0;
    }

    /// <summary>
    /// Raises <see cref="Changed"/> once for each marked directory, clears the marks and cancels a pending coalescing timer.
    /// </summary>
    public void Flush()
    {
        _flush?.Remove();
        _flush = null;
        _flushing.Clear();
        _flushing.AddRange(_dirty);
        _dirty.Clear();
        foreach (var path in _flushing)
        {
            Changed?.Invoke(path);
        }

        _flushing.Clear();
    }

    /// <summary>
    /// Removes the loop source and the timer, and closes the descriptor.
    /// </summary>
    public void Dispose()
    {
        _flush?.Remove();
        _flush = null;
        _source?.Remove();
        if (_fd >= 0)
        {
            _ = close(_fd);
        }
    }

    private void OnReadable(int fd, FdReadiness readiness)
    {
        if (Read() && _flush is null && _loop is not null)
        {
            _flush = _loop.AddTimer(Flush);
            _flush.UpdateTimer(CoalesceMillis);
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int inotify_init1(int flags);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int inotify_add_watch(int fd, string path, uint mask);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int inotify_rm_watch(int fd, int wd);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint read_fd(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int close(int fd);
}
