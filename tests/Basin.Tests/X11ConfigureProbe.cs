using System.Runtime.InteropServices;
using Xcb.Native;
using Xunit;

namespace Basin.Tests;

internal sealed unsafe class X11ConfigureProbe : IDisposable
{
    private const uint EventMask = 0x800;
    private const uint StructureNotifyMask = 0x20000;
    private const uint SubstructureRedirectAndNotify = 0x180000;
    private const byte ConfigureNotify = 22;
    private const byte ClientMessage = 33;
    private const uint AtomType = 4;

    private readonly xcb_connection_t* _conn;
    private readonly uint _window;
    private readonly uint _root;
    private readonly uint _state;
    private readonly uint _fullscreen;

    public X11ConfigureProbe(string display, uint window)
        : this(Connect(display), window)
    {
        var mask = StructureNotifyMask;
        _ = Libxcb.xcb_change_window_attributes(_conn, window, EventMask, &mask);
        RoundTrip();
    }

    private X11ConfigureProbe(xcb_connection_t* conn, uint window)
    {
        _conn = conn;
        _window = window;
        _root = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(_conn)).data->root;
        _state = Intern("_NET_WM_STATE"u8);
        _fullscreen = Intern("_NET_WM_STATE_FULLSCREEN"u8);
    }

    private static xcb_connection_t* Connect(string display)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(display + "\0");
        xcb_connection_t* conn;
        fixed (byte* ptr = bytes)
        {
            conn = Libxcb.xcb_connect((sbyte*)ptr, null);
        }

        Assert.Equal(0, Libxcb.xcb_connection_has_error(conn));
        return conn;
    }

    public List<(int Width, int Height)> Sizes { get; } = [];

    public bool StateSaysFullscreen
    {
        get
        {
            var reply = Libxcb.xcb_get_property_reply(_conn, Libxcb.xcb_get_property(_conn, 0, _window, _state, AtomType, 0, 32), null);
            Assert.True(reply != null);
            var count = Libxcb.xcb_get_property_value_length(reply) / 4;
            var atoms = (uint*)Libxcb.xcb_get_property_value(reply);
            var found = false;
            for (var i = 0; i < count; i++)
            {
                found |= atoms[i] == _fullscreen;
            }

            NativeMemory.Free(reply);
            return found;
        }
    }

    public void RequestFullscreen(bool fullscreen)
    {
        var message = new xcb_client_message_event_t
        {
            response_type = ClientMessage,
            format = 32,
            window = _window,
            type = _state,
        };
        var data = (uint*)&message.data;
        data[0] = fullscreen ? 1u : 0u;
        data[1] = _fullscreen;
        data[3] = 1;
        _ = Libxcb.xcb_send_event(_conn, 0, _root, SubstructureRedirectAndNotify, (sbyte*)&message);
        RoundTrip();
    }

    public void Drain()
    {
        RoundTrip();
        while (Libxcb.xcb_poll_for_event(_conn) is var ev && ev != null)
        {
            if ((ev->response_type & 0x7f) == ConfigureNotify)
            {
                var notify = (xcb_configure_notify_event_t*)ev;
                Sizes.Add((notify->width, notify->height));
            }

            NativeMemory.Free(ev);
        }
    }

    public static uint MapFullscreenWindow(string display, out X11ConfigureProbe owner)
    {
        var conn = Connect(display);
        var screen = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(conn)).data;
        var window = Libxcb.xcb_generate_id(conn);
        var values = stackalloc uint[2] { 0x336699, StructureNotifyMask };
        _ = Libxcb.xcb_create_window(conn, 0, window, screen->root, 0, 0, 200, 150, 0, 1, screen->root_visual, 0x2 | 0x800, values);
        owner = new X11ConfigureProbe(conn, window);
        var fullscreen = owner._fullscreen;
        _ = Libxcb.xcb_change_property(conn, 0, window, owner._state, AtomType, 32, 1, &fullscreen);
        _ = Libxcb.xcb_map_window(conn, window);
        owner.RoundTrip();
        return window;
    }

    public void Dispose() => Libxcb.xcb_disconnect(_conn);

    private uint Intern(ReadOnlySpan<byte> name)
    {
        fixed (byte* ptr = name)
        {
            var reply = Libxcb.xcb_intern_atom_reply(_conn, Libxcb.xcb_intern_atom(_conn, 0, (ushort)name.Length, (sbyte*)ptr), null);
            var atom = reply->atom;
            NativeMemory.Free(reply);
            return atom;
        }
    }

    private void RoundTrip()
    {
        var reply = Libxcb.xcb_get_input_focus_reply(_conn, Libxcb.xcb_get_input_focus(_conn), null);
        NativeMemory.Free(reply);
    }
}
