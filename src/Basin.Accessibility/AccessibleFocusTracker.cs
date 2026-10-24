using Basin.Capabilities;
using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.Accessibility.AccessibilityLog;

namespace Basin.Accessibility;

/// <summary>
/// Listens on the accessibility bus for focus and caret events of every application and reports them in layout coordinates.
/// A Wayland client reports boxes relative to its window, so the tracker finds the window through the client's process id.
/// </summary>
public sealed class AccessibleFocusTracker : IAccessibleFocus, IDisposable
{
    private const string ObjectEvents = "org.a11y.atspi.Event.Object";
    private const uint ScreenCoordinates = 0;
    private const uint WindowCoordinates = 1;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly CompositorSynchronizationContext _context;
    private readonly IToplevelModel _toplevels;
    private readonly string _sessionAddress;
    private readonly Dictionary<string, uint> _pids = [];
    private readonly CancellationTokenSource _stop = new();
    private DBusConnection? _a11y;
    private bool _disposed;

    private AccessibleFocusTracker(ICompositorEventLoop loop, IToplevelModel toplevels, string sessionAddress)
    {
        _context = new CompositorSynchronizationContext(loop);
        _toplevels = toplevels;
        _sessionAddress = sessionAddress;
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public event Action<Box, AccessibleFocusKind>? Moved;

    /// <summary>
    /// Gets a task that completes when the tracker listens, or faults when the accessibility bus is not reachable.
    /// </summary>
    public Task Started { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts to listen. The connection is made in the background, so the call does not block. Call it on the loop thread.
    /// </summary>
    /// <param name="loop">The compositor loop.</param>
    /// <param name="toplevels">The windows, to map a box that is relative to a window.</param>
    /// <param name="sessionAddress">The address of the session bus, or <see langword="null"/> for <c>DBUS_SESSION_BUS_ADDRESS</c>.</param>
    /// <param name="reason">The reason when the method returns <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The tracker, which must be disposed, or <see langword="null"/> when the session has no bus.</returns>
    public static AccessibleFocusTracker? TryStart(ICompositorEventLoop loop, IToplevelModel toplevels, string? sessionAddress, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(toplevels);
        var address = sessionAddress is { Length: > 0 } given ? given : Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            reason = "this session has no D-Bus session bus";
            Log.Info($"focus: not started, {reason}");
            return null;
        }

        var tracker = new AccessibleFocusTracker(loop, toplevels, address);
        tracker.Started = Task.Run(tracker.ListenAsync);
        reason = null;
        return tracker;
    }

    /// <summary>
    /// Stops listening and closes the connection. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        _stop.Cancel();
        Interlocked.Exchange(ref _a11y, null)?.Dispose();
        _context.Dispose();
        _stop.Dispose();
        BasinCounters.Untrack();
    }

    private static MessageBuffer RegisterEvent(DBusConnection connection, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.atspi.Registry", "/org/a11y/atspi/registry", "org.a11y.atspi.Registry", "RegisterEvent", "sass");
        writer.WriteString(name);
        writer.WriteArray(Array.Empty<string>());
        writer.WriteString("");
        return writer.CreateMessage();
    }

    private static MessageBuffer Extents(DBusConnection connection, string bus, string path, uint coordinates)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, "org.a11y.atspi.Component", "GetExtents", "u");
        writer.WriteUInt32(coordinates);
        return writer.CreateMessage();
    }

    private static MessageBuffer CharacterExtents(DBusConnection connection, string bus, string path, int offset, uint coordinates)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(bus, path, "org.a11y.atspi.Text", "GetCharacterExtents", "iu");
        writer.WriteInt32(offset);
        writer.WriteUInt32(coordinates);
        return writer.CreateMessage();
    }

    private static Box ReadBox(Message message, bool flat)
    {
        var reader = message.GetBodyReader();
        if (!flat)
        {
            reader.AlignStruct();
        }

        return new Box(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
    }

    private async Task ListenAsync()
    {
        try
        {
            var a11y = await A11yBus.ConnectAsync(_sessionAddress).ConfigureAwait(false);
            if (Interlocked.CompareExchange(ref _a11y, a11y, null) is not null || _stop.IsCancellationRequested)
            {
                a11y.Dispose();
                return;
            }

            await a11y.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = ObjectEvents },
                static (m, _) =>
                {
                    var reader = m.GetBodyReader();
                    var kind = reader.ReadString();
                    var detail1 = reader.ReadInt32();
                    return (m.SenderAsString ?? "", m.PathAsString ?? "", m.MemberAsString ?? "", kind, detail1);
                },
                (Notification<(string Sender, string Path, string Member, string Kind, int Detail)> n) =>
                {
                    if (n.HasValue)
                    {
                        OnEvent(n.Value.Sender, n.Value.Path, n.Value.Member, n.Value.Kind, n.Value.Detail);
                    }
                },
                false,
                ObserverFlags.None,
                null).ConfigureAwait(false);
            foreach (var name in new[] { "object:state-changed:focused", "object:text-caret-moved" })
            {
                try
                {
                    await a11y.CallMethodAsync(RegisterEvent(a11y, name)).ConfigureAwait(false);
                }
                catch (DBusErrorReplyException error)
                {
                    Log.Debug(error, $"focus: the registry did not take {name}");
                }
            }

            Log.Info($"focus: listening on the accessibility bus");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log.Info($"focus: not started, the accessibility bus is not reachable: {error.Message}");
            throw;
        }
    }

    private void OnEvent(string sender, string path, string member, string kind, int detail)
    {
        var caret = member == "TextCaretMoved";
        if (!caret && !(member == "StateChanged" && kind == "focused" && detail == 1))
        {
            return;
        }

        _ = Task.Run(() => LocateAsync(sender, path, caret ? AccessibleFocusKind.Caret : AccessibleFocusKind.Focus, detail));
    }

    private async Task LocateAsync(string sender, string path, AccessibleFocusKind kind, int offset)
    {
        if (_a11y is not { } a11y)
        {
            return;
        }

        try
        {
            uint pid;
            lock (_pids)
            {
                _pids.TryGetValue(sender, out pid);
            }

            if (pid == 0)
            {
                pid = await A11yBus.PidOfAsync(a11y, sender).ConfigureAwait(false);
                lock (_pids)
                {
                    if (_pids.Count > 256)
                    {
                        _pids.Clear();
                    }

                    _pids[sender] = pid;
                }
            }

            var own = pid == (uint)Environment.ProcessId;
            var coordinates = own ? ScreenCoordinates : WindowCoordinates;
            var box = kind == AccessibleFocusKind.Caret
                ? await a11y.CallMethodAsync(CharacterExtents(a11y, sender, path, offset, coordinates), static (m, _) => ReadBox(m, flat: true), null).ConfigureAwait(false)
                : await a11y.CallMethodAsync(Extents(a11y, sender, path, coordinates), static (m, _) => ReadBox(m, flat: false), null).ConfigureAwait(false);
            if (box.Width < 0 || box.Height < 0)
            {
                return;
            }

            _context.Post(_ => Report(pid, own, box, kind), null);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log.Debug(error, $"focus: the {kind} of {sender} at {path} has no extents");
        }
    }

    private void Report(uint pid, bool own, Box box, AccessibleFocusKind kind)
    {
        if (_disposed)
        {
            return;
        }

        if (!own)
        {
            if (WindowOf(pid) is not { } window)
            {
                return;
            }

            box = box.Translated(window.X, window.Y);
        }

        Moved?.Invoke(box, kind);
    }

    private Box? WindowOf(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }

        var length = 64;
        while (true)
        {
            var toplevels = System.Buffers.ArrayPool<ToplevelInfo>.Shared.Rent(length);
            try
            {
                var count = _toplevels.Enumerate(toplevels);
                if (count < 0)
                {
                    length = toplevels.Length * 2;
                    continue;
                }

                Box? found = null;
                for (var i = 0; i < count; i++)
                {
                    var toplevel = toplevels[i];
                    if (toplevel.Pid != pid)
                    {
                        continue;
                    }

                    var origin = toplevel.ClientGeometry.IsEmpty ? toplevel.Geometry : toplevel.ClientGeometry;
                    if ((toplevel.State & ToplevelState.Activated) != 0)
                    {
                        return origin;
                    }

                    found ??= origin;
                }

                return found;
            }
            finally
            {
                System.Buffers.ArrayPool<ToplevelInfo>.Shared.Return(toplevels, clearArray: true);
            }
        }
    }
}
