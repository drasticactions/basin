using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal.DBus;
using Microsoft.Win32.SafeHandles;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// The clipboard of one portal session. It sets and reads the compositor clipboard through <see cref="ISelectionStore"/>, and moves data through file descriptors.
/// </summary>
public sealed class PortalClipboard
{
    private readonly PortalSession _session;
    private readonly Dictionary<uint, ClientFd> _parked = [];
    private ISelectionStore? _store;
    private DataSource? _source;
    private uint _serial;

    internal PortalClipboard(PortalSession session) => _session = session;

    /// <summary>
    /// Gets a value indicating whether the application called <c>RequestClipboard</c> before the session started.
    /// </summary>
    public bool Requested { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the start granted the clipboard. The session receives owner changes and transfers only when it is set.
    /// </summary>
    public bool Enabled { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the last selection that this session set is still the compositor clipboard.
    /// </summary>
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

    /// <summary>
    /// Makes this session the owner of the compositor clipboard. A paste from another client raises <c>SelectionTransfer</c> on the bus.
    /// </summary>
    /// <param name="mimeTypes">The MIME types that the session offers. The list is copied.</param>
    /// <returns><see langword="false"/> when the clipboard is not attached or the store refuses the selection.</returns>
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

    /// <summary>
    /// Gives the session the file descriptor of a pending transfer, so that it can write the data.
    /// </summary>
    /// <param name="serial">The serial from the <c>SelectionTransfer</c> signal.</param>
    /// <returns>A duplicate of the reader's descriptor that the caller owns.</returns>
    /// <exception cref="Tmds.DBus.Protocol.DBusErrorReplyException">The serial names no pending transfer, or the descriptor cannot be duplicated.</exception>
    public SafeHandle SelectionWrite(uint serial)
    {
        if (!_parked.TryGetValue(serial, out var fd))
        {
            throw PortalError.Invalid($"Serial {serial} names no pending transfer.");
        }

        var duplicate = dup(fd.Value);
        if (duplicate < 0)
        {
            throw PortalError.Invalid($"The transfer fd for serial {serial} cannot be duplicated.");
        }

        return new SafeFileHandle(duplicate, ownsHandle: true);
    }

    /// <summary>
    /// Closes the descriptor of a pending transfer. An unknown serial is ignored.
    /// </summary>
    /// <param name="serial">The serial from the <c>SelectionTransfer</c> signal.</param>
    /// <param name="success">The result that the session reports. The compositor does not use it.</param>
    public void SelectionWriteDone(uint serial, bool success)
    {
        _ = success;
        if (_parked.Remove(serial, out var fd))
        {
            fd.Close();
        }
    }

    /// <summary>
    /// Opens a pipe and asks the clipboard owner to write one MIME type into it. With no owner, the pipe reaches end of file at once.
    /// </summary>
    /// <param name="mimeType">The MIME type to read.</param>
    /// <returns>The read end of the pipe, which the caller owns.</returns>
    /// <exception cref="Tmds.DBus.Protocol.DBusErrorReplyException">The pipe cannot be created.</exception>
    public SafeHandle SelectionRead(string mimeType)
    {
        int readEnd, writeEnd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (pipe2(fds, OCloexec) != 0)
            {
                throw PortalError.Invalid("The read pipe cannot be created.");
            }

            readEnd = fds[0];
            writeEnd = fds[1];
        }

        if (_store is not { } store || !store.Receive(SelectionKind.Clipboard, mimeType, new ClientFd(writeEnd, null)))
        {
            Log.Debug($"clipboard: read of {mimeType} for session {_session.Id} has no owner");
        }

        return new SafeFileHandle(readEnd, ownsHandle: true);
    }

    private void OnSend(string mimeType, ClientFd fd)
    {
        if (fd.Owner?.FdSlots is not null)
        {
            Log.Warn($"clipboard: transfer for session {_session.Id} refused, the reader's fd is a slot, not a descriptor");
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
            Log.Warn(e, $"clipboard: transfer for session {_session.Id} not announced");
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
            Log.Debug(e, $"clipboard: owner change for session {_session.Id} not announced");
        }
    }

    private const int OCloexec = 0x80000;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup(int fd);
}
