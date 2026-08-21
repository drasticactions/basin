using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Basin.Seat;
using Xcb.Native;

namespace Basin.XWayland;

internal sealed unsafe class XWaylandClipboard : IDisposable
{
    private const byte EventSelectionNotify = 31;
    private const byte EventSelectionRequest = 30;
    private const byte EventSelectionClear = 29;
    private const int PropertyMax = 256 * 1024;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly xcb_connection_t* _conn;
    private readonly Seat.Seat _seat;
    private readonly ICompositorEventLoop _loop;
    private readonly uint _window;
    private readonly uint _root;
    private readonly Func<string, uint> _atom;
    private readonly byte _xfixesEventBase;
    private readonly List<PendingSend> _pendingSends = [];
    private DataSource? _fromX;
    private DataSource? _wlSourceForX;
    private bool _settingFromX;

    private sealed class PendingSend
    {
        public uint Requestor;
        public uint Property;
        public uint Target;
        public uint Selection;
        public uint Time;
        public int ReadFd;
        public IEventSource Source = null!;
        public readonly MemoryStream Buffer = new();
    }

    public XWaylandClipboard(xcb_connection_t* conn, Seat.Seat seat, ICompositorEventLoop loop, uint root, uint rootVisual, Func<string, uint> atom)
    {
        _conn = conn;
        _seat = seat;
        _loop = loop;
        _root = root;
        _atom = atom;

        _window = Libxcb.xcb_generate_id(conn);
        var mask = (uint)0x00400000 ;
        var values = stackalloc uint[1] { mask };
        _ = Libxcb.xcb_create_window(
            conn, 0, _window, root, 0, 0, 1, 1, 0,
            (ushort)xcb_window_class_t.XCB_WINDOW_CLASS_INPUT_OUTPUT, rootVisual, 0x800 , values);

        var query = LibxcbXfixes.xcb_xfixes_query_version(conn, 5, 0);
        var reply = LibxcbXfixes.xcb_xfixes_query_version_reply(conn, query, null);
        _xfixesEventBase = QueryXfixesEventBase(conn);
        if (reply != null)
        {
            Libc.Free(reply);
        }

        LibxcbXfixes.xcb_xfixes_select_selection_input(
            conn, _window, atom("CLIPBOARD"),
            1 | 2 | 4 );

        _seat.DataDevice.SelectionChanged += OnWaylandSelectionChanged;

        if (_seat.DataDevice.Selection is { } current)
        {
            OnWaylandSelectionChanged(current);
        }
    }

    public void Dispose()
    {
        _seat.DataDevice.SelectionChanged -= OnWaylandSelectionChanged;
    }

    public bool Handle(xcb_generic_event_t* ev)
    {
        var type = (byte)(ev->response_type & 0x7F);
        if (type == _xfixesEventBase)
        {
            var e = (xcb_xfixes_selection_notify_event_t*)ev;
            if (e->selection == _atom("CLIPBOARD"))
            {
                OnXSelectionOwnerChanged(e->owner);
                return true;
            }

            return false;
        }

        switch (type)
        {
            case EventSelectionNotify:
                return true;
            case EventSelectionRequest:
                AnswerSelectionRequest((xcb_selection_request_event_t*)ev);
                return true;
            case EventSelectionClear:
                _wlSourceForX = null;
                return true;
            default:
                return false;
        }
    }

    private void OnXSelectionOwnerChanged(uint owner)
    {
        if (owner == 0 || owner == _window)
        {
            return;
        }

        var mimes = FetchXTargets();
        if (mimes.Count == 0)
        {
            return;
        }

        var source = new DataSource(mimes, SendXSelectionToFd);
        _fromX = source;
        _settingFromX = true;
        _seat.DataDevice.SetSelection(source);
        _settingFromX = false;
    }

    private List<string> FetchXTargets()
    {
        _ = Libxcb.xcb_convert_selection(
            _conn, _window, _atom("CLIPBOARD"), _atom("TARGETS"), _atom("_BASIN_SELECTION"), 0);
        _ = Libxcb.xcb_flush(_conn);
        if (!WaitForSelectionNotify(out var property) || property == 0)
        {
            return [];
        }

        var atoms = ReadProperty32(property);
        var mimes = new List<string>();
        var haveText = false;
        foreach (var atomId in atoms)
        {
            if (atomId == _atom("UTF8_STRING") || atomId == _atom("TEXT") || atomId == 31 )
            {
                haveText = true;
            }
        }

        if (haveText)
        {
            mimes.Add("text/plain;charset=utf-8");
            mimes.Add("text/plain");
        }

        return mimes;
    }

    private void SendXSelectionToFd(string mimeType, ClientFd fd)
    {
        _ = Libxcb.xcb_convert_selection(
            _conn, _window, _atom("CLIPBOARD"), _atom("UTF8_STRING"), _atom("_BASIN_SELECTION"), 0);
        _ = Libxcb.xcb_flush(_conn);
        if (WaitForSelectionNotify(out var property) && property != 0)
        {
            var data = ReadProperty8(property);
            fixed (byte* ptr = data)
            {
                var offset = 0;
                while (offset < data.Length)
                {
                    var written = (int)write(fd.Value, ptr + offset, (nuint)(data.Length - offset));
                    if (written <= 0)
                    {
                        break;
                    }

                    offset += written;
                }
            }
        }

        fd.Close();
    }

    private void OnWaylandSelectionChanged(DataSource? source)
    {
        if (_settingFromX)
        {
            return;
        }

        _fromX = null;
        if (source is null)
        {
            _wlSourceForX = null;
            return;
        }

        _wlSourceForX = source;
        _ = Libxcb.xcb_set_selection_owner(_conn, _window, _atom("CLIPBOARD"), 0);
        _ = Libxcb.xcb_flush(_conn);
    }

    private void AnswerSelectionRequest(xcb_selection_request_event_t* e)
    {
        var property = e->property != 0 ? e->property : e->target;

        if (_wlSourceForX is not { } source)
        {
            SendSelectionNotify(e->requestor, e->selection, e->target, e->time, 0);
            return;
        }

        if (e->target == _atom("TARGETS"))
        {
            var targets = stackalloc uint[3] { _atom("TARGETS"), _atom("UTF8_STRING"), 31 };
            _ = Libxcb.xcb_change_property(_conn, 0, e->requestor, property, 4 , 32, 3, targets);
            SendSelectionNotify(e->requestor, e->selection, e->target, e->time, property);
            return;
        }

        if (e->target != _atom("UTF8_STRING") && e->target != _atom("TEXT") && e->target != 31)
        {
            SendSelectionNotify(e->requestor, e->selection, e->target, e->time, 0);
            return;
        }

        var mime = source.MimeTypes.Contains("text/plain;charset=utf-8") ? "text/plain;charset=utf-8"
            : source.MimeTypes.Contains("text/plain") ? "text/plain" : null;
        if (mime is null)
        {
            SendSelectionNotify(e->requestor, e->selection, e->target, e->time, 0);
            return;
        }

        var fds = stackalloc int[2];
        if (pipe2(fds, 0) != 0)
        {
            SendSelectionNotify(e->requestor, e->selection, e->target, e->time, 0);
            return;
        }

        var readFd = fds[0];
        var pending = new PendingSend
        {
            Requestor = e->requestor,
            Property = property,
            Target = e->target,
            Selection = e->selection,
            Time = e->time,
            ReadFd = readFd,
        };
        pending.Source = _loop.AddFd(readFd, FdReadiness.Readable, (_, _) => DrainPending(pending));
        _pendingSends.Add(pending);

        source.Send(mime, new ClientFd(fds[1], null));
    }

    private void DrainPending(PendingSend pending)
    {
        var scratch = stackalloc byte[4096];
        var got = (int)read(pending.ReadFd, scratch, 4096);
        if (got > 0)
        {
            for (var i = 0; i < got; i++)
            {
                pending.Buffer.WriteByte(scratch[i]);
            }

            if (pending.Buffer.Length <= PropertyMax)
            {
                return;
            }
        }

        pending.Source.Remove();
        close(pending.ReadFd);
        _pendingSends.Remove(pending);

        var data = pending.Buffer.ToArray();
        fixed (byte* ptr = data)
        {
            _ = Libxcb.xcb_change_property(
                _conn, 0, pending.Requestor, pending.Property, _atom("UTF8_STRING"), 8, (uint)data.Length, ptr);
        }

        SendSelectionNotify(pending.Requestor, pending.Selection, pending.Target, pending.Time, pending.Property);
    }

    private void SendSelectionNotify(uint requestor, uint selection, uint target, uint time, uint property)
    {
        var notify = new xcb_selection_notify_event_t
        {
            response_type = EventSelectionNotify,
            time = time,
            requestor = requestor,
            selection = selection,
            target = target,
            property = property,
        };
        _ = Libxcb.xcb_send_event(_conn, 0, requestor, 0, (sbyte*)&notify);
        _ = Libxcb.xcb_flush(_conn);
    }

    private bool WaitForSelectionNotify(out uint property)
    {
        property = 0;
        for (var i = 0; i < 1000; i++)
        {
            var ev = Libxcb.xcb_wait_for_event(_conn);
            if (ev == null)
            {
                return false;
            }

            var type = (byte)(ev->response_type & 0x7F);
            if (type == EventSelectionNotify)
            {
                var e = (xcb_selection_notify_event_t*)ev;
                property = e->property;
                Libc.Free(ev);
                return true;
            }

            if (type == EventSelectionRequest)
            {
                AnswerSelectionRequest((xcb_selection_request_event_t*)ev);
            }

            Libc.Free(ev);
        }

        return false;
    }

    private uint[] ReadProperty32(uint property)
    {
        var cookie = Libxcb.xcb_get_property(_conn, 0, _window, property, 0 , 0, PropertyMax / 4);
        var reply = Libxcb.xcb_get_property_reply(_conn, cookie, null);
        if (reply == null)
        {
            return [];
        }

        var count = Libxcb.xcb_get_property_value_length(reply) / 4;
        var value = (uint*)Libxcb.xcb_get_property_value(reply);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = value[i];
        }

        Libc.Free(reply);
        _ = Libxcb.xcb_delete_property(_conn, _window, property);
        return result;
    }

    private byte[] ReadProperty8(uint property)
    {
        var cookie = Libxcb.xcb_get_property(_conn, 0, _window, property, 0, 0, PropertyMax / 4);
        var reply = Libxcb.xcb_get_property_reply(_conn, cookie, null);
        if (reply == null)
        {
            return [];
        }

        var length = Libxcb.xcb_get_property_value_length(reply);
        var value = (byte*)Libxcb.xcb_get_property_value(reply);
        var result = new byte[length];
        Marshal.Copy((nint)value, result, 0, length);
        Libc.Free(reply);
        _ = Libxcb.xcb_delete_property(_conn, _window, property);
        return result;
    }

    private static byte QueryXfixesEventBase(xcb_connection_t* conn)
    {
        var cookie = Libxcb.xcb_query_extension(conn, 6, ToSbyte("XFIXES"));
        var reply = Libxcb.xcb_query_extension_reply(conn, cookie, null);
        var eventBase = reply != null ? reply->first_event : (byte)0;
        if (reply != null)
        {
            Libc.Free(reply);
        }

        return eventBase;
    }

    private static sbyte* ToSbyte(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var ptr = (sbyte*)Marshal.AllocHGlobal(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            ptr[i] = (sbyte)bytes[i];
        }

        return ptr;
    }
}
