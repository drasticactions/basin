#!/usr/bin/env python3
"""Offer a clipboard selection over the Clipboard portal, serve the bytes.

The Clipboard portal rides on a RemoteDesktop (or InputCapture) session. This
takes a RemoteDesktop session through the frontend, requests the clipboard,
starts (which prompts the compositor), sets a text/plain selection, and then
serves the bytes whenever something pastes: on SelectionTransfer it calls
SelectionWrite for the fd, writes TEXT, and SelectionWriteDone. A `wl-paste -t
text/plain` in the compositor session then reads TEXT.

Usage: portal-clip.py [TEXT] [SECONDS]   (default "portal clipboard", 30s)
"""
import os
import sys
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

RD = "org.freedesktop.portal.RemoteDesktop"
CLIP = "org.freedesktop.portal.Clipboard"
DEST = "org.freedesktop.portal.Desktop"
PATH = "/org/freedesktop/portal/desktop"
MIME = "text/plain;charset=utf-8"

text = (sys.argv[1] if len(sys.argv) > 1 else "portal clipboard").encode()
secs = int(sys.argv[2]) if len(sys.argv) > 2 else 30

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
sender = bus.get_unique_name().lstrip(":").replace(".", "_")
loop = GLib.MainLoop()
token_n = 0
_responses = {}
_waiters = {}


def _on_response(conn, s, obj, i, sname, params):
    _responses[obj] = params.unpack()
    cb = _waiters.pop(obj, None)
    if cb:
        cb()


bus.signal_subscribe(DEST, "org.freedesktop.portal.Request", "Response",
                     None, None, Gio.DBusSignalFlags.NONE, _on_response)


def call(iface, member, sig, args):
    global token_n
    token_n += 1
    reply = bus.call_sync(DEST, PATH, iface, member, GLib.Variant(sig, args),
                          None, Gio.DBusCallFlags.NONE, 60000, None)
    req = reply.unpack()[0]
    if req in _responses:
        return _responses.pop(req)
    _waiters[req] = loop.quit
    tid = GLib.timeout_add(90000, lambda: (loop.quit(), False)[1])
    loop.run()
    GLib.source_remove(tid)
    return _responses.pop(req, (None, {}))


def plain(iface, member, sig, args):
    return bus.call_sync(DEST, PATH, iface, member, GLib.Variant(sig, args),
                         None, Gio.DBusCallFlags.NONE, 10000, None).unpack()


def on_transfer(conn, s, obj, i, sname, params):
    session_handle, mime, serial = params.unpack()
    reply, fds = bus.call_with_unix_fd_list_sync(
        DEST, PATH, CLIP, "SelectionWrite",
        GLib.Variant("(ou)", (session_handle, serial)), None,
        Gio.DBusCallFlags.NONE, 5000, None, None)
    idx = reply.unpack()[0]
    fd = fds.get(idx)
    os.write(fd, text)
    os.close(fd)
    plain(CLIP, "SelectionWriteDone", "(oub)", (session_handle, serial, True))
    print(f"served {mime} serial {serial}: {text!r}", flush=True)


def main():
    session = f"{PATH}/session/{sender}/clip"
    code, res = call(RD, "CreateSession", "(a{sv})",
                     ({"session_handle_token": GLib.Variant("s", "clip"),
                       "handle_token": GLib.Variant("s", "c1")},))
    session = (res or {}).get("session_handle", session)
    print("CreateSession", code, flush=True)
    call(RD, "SelectDevices", "(oa{sv})",
         (session, {"types": GLib.Variant("u", 3), "handle_token": GLib.Variant("s", "c2")}))
    plain(CLIP, "RequestClipboard", "(oa{sv})", (session, {}))
    code, res = call(RD, "Start", "(osa{sv})",
                     (session, "", {"handle_token": GLib.Variant("s", "c3")}))
    print("Start", code, "clipboard_enabled=", (res or {}).get("clipboard_enabled"), flush=True)
    if code != 0:
        return

    bus.signal_subscribe(DEST, CLIP, "SelectionTransfer", PATH, None,
                         Gio.DBusSignalFlags.NONE, on_transfer)
    plain(CLIP, "SetSelection", "(oa{sv})",
          (session, {"mime_types": GLib.Variant("as", [MIME])}))
    print(f"offered {MIME}; paste with wl-paste. Serving {secs}s.", flush=True)
    GLib.timeout_add(secs * 1000, lambda: (loop.quit(), False)[1])
    loop.run()


main()
