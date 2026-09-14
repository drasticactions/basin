using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal.DBus;
using Microsoft.Win32.SafeHandles;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalClipboard
{
    private readonly PortalSession _session;
    private readonly Dictionary<uint, ClientFd> _parked = [];
    private ISelectionStore? _store;
    private DataSource? _source;
    private uint _serial;

    internal PortalClipboard(PortalSession session) => _session = session;

    public bool Requested { get; internal set; }

    public bool Enabled { get; internal set; }

    public bool IsOwner => _store is { } store && _source is { } source && ReferenceEquals(store.Current(SelectionKind.Clipboard), source);

    internal void Attach(ISelectionStore store)
    {
        if (_store is not null)
        {
            return;
        }

        _store = store;
        store.SelectionChanged += OnSelectionChanged;
    }

    internal void Detach()
    {
        if (_store is not { } store)
        {
            return;
        }

        store.SelectionChanged -= OnSelectionChanged;
        if (_source is { } source && ReferenceEquals(store.Current(SelectionKind.Clipboard), source))
        {
            _ = store.SetSelection(SelectionKind.Clipboard, null, SelectionSerial.Unchecked);
        }

        _source?.MarkDestroyed();
        _source = null;
        foreach (var fd in _parked.Values)
        {
            fd.Close();
        }

        _parked.Clear();
        _store = null;
    }

    public bool SetSelection(IReadOnlyList<string> mimeTypes)
    {
        if (_store is not { } store)
        {
            return false;
        }

        _source?.MarkDestroyed();
        var source = new DataSource(new List<string>(mimeTypes), OnSend, OnCancel);
        _source = source;
        if (!store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked))
        {
            _source = null;
            source.MarkDestroyed();
            return false;
        }

        return true;
    }

    public SafeHandle SelectionWrite(uint serial)
    {
        if (!_parked.TryGetValue(serial, out var fd))
        {
            throw PortalError.Invalid($"serial {serial} names no pending transfer");
        }

        var duplicate = dup(fd.Value);
        if (duplicate < 0)
        {
            throw PortalError.Invalid($"the transfer fd for serial {serial} could not be duplicated");
        }

        return new SafeFileHandle(duplicate, ownsHandle: true);
    }

    public void SelectionWriteDone(uint serial, bool success)
    {
        _ = success;
        if (_parked.Remove(serial, out var fd))
        {
            fd.Close();
        }
    }

    public SafeHandle SelectionRead(string mimeType)
    {
        int readEnd, writeEnd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (pipe2(fds, OCloexec) != 0)
            {
                throw PortalError.Invalid("the read pipe could not be created");
            }

            readEnd = fds[0];
            writeEnd = fds[1];
        }

        if (_store is not { } store || !store.Receive(SelectionKind.Clipboard, mimeType, new ClientFd(writeEnd, null)))
        {
            Log.Debug($"clipboard read of {mimeType} for session {_session.Id}: no owner");
        }

        return new SafeFileHandle(readEnd, ownsHandle: true);
    }

    private void OnSend(string mimeType, ClientFd fd)
    {
        if (fd.Owner?.FdSlots is not null)
        {
            Log.Warn($"clipboard transfer for session {_session.Id} refused: the reader's fd is a slot, not a descriptor");
            fd.Close();
            return;
        }

        var serial = ++_serial;
        _parked[serial] = fd;
        try
        {
            _session.Bus.Connection?.EmitSelectionTransfer(new Tmds.DBus.Protocol.ObjectPath(PortalBus.RootPath), _session.Handle, mimeType, serial);
        }
        catch (Exception e) when (e is Tmds.DBus.Protocol.DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Warn($"clipboard transfer for session {_session.Id} not announced: {e.Message}");
            _parked.Remove(serial);
            fd.Close();
        }
    }

    private void OnCancel()
    {
    }

    private void OnSelectionChanged(SelectionKind kind)
    {
        if (kind != SelectionKind.Clipboard || !Enabled || _store is not { } store)
        {
            return;
        }

        var types = new string[64];
        var count = store.GetOffer(SelectionKind.Clipboard, types);
        while (count < 0)
        {
            types = new string[types.Length * 2];
            count = store.GetOffer(SelectionKind.Clipboard, types);
        }

        var options = new Dictionary<string, Tmds.DBus.Protocol.VariantValue>
        {
            ["mime_types"] = Tmds.DBus.Protocol.VariantValue.Array(types.AsSpan(0, count).ToArray()),
            ["session_is_owner"] = Tmds.DBus.Protocol.VariantValue.Bool(IsOwner),
        };
        try
        {
            _session.Bus.Connection?.EmitSelectionOwnerChanged(new Tmds.DBus.Protocol.ObjectPath(PortalBus.RootPath), _session.Handle, options);
        }
        catch (Exception e) when (e is Tmds.DBus.Protocol.DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"clipboard owner change for session {_session.Id} not announced: {e.Message}");
        }
    }

    private const int OCloexec = 0x80000;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);
}
