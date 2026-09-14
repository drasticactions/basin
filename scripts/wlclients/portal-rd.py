#!/usr/bin/env python3
"""Drive the RemoteDesktop portal through the frontend and inject input.

Takes the org.freedesktop.portal.RemoteDesktop portal, selects keyboard and
pointer, starts the session (which prompts the compositor), then injects the
legacy NotifyPointerMotion / NotifyKeyboardKeycode path so a compositor without
a live EIS consumer still receives events. With --clipboard it also exercises
the Clipboard portal that rides on the same session: it sets a text/plain
offer and writes bytes when asked.

Usage: portal-rd.py [--clipboard] [TEXT]
  TEXT is typed as evdev keycodes; default "hello".
"""
import sys
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

RD = "org.freedesktop.portal.RemoteDesktop"
CLIP = "org.freedesktop.portal.Clipboard"
DEST = "org.freedesktop.portal.Desktop"
PATH = "/org/freedesktop/portal/desktop"

# evdev keycodes for a-z and space, enough for a demo string.
KEYS = {c: 16 + i for i, c in enumerate("qwertyuiop")}
KEYS.update({c: 30 + i for i, c in enumerate("asdfghjkl")})
KEYS.update({c: 44 + i for i, c in enumerate("zxcvbnm")})
KEYS[" "] = 57

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
sender = bus.get_unique_name().lstrip(":").replace(".", "_")
loop = GLib.MainLoop()
token_n = 0

# One persistent subscription to every Request.Response, keyed by object path.
_responses = {}
_waiters = {}


def _on_response(conn, s, obj, i, sname, params):
    code, results = params.unpack()
    _responses[obj] = (code, results)
    cb = _waiters.pop(obj, None)
    if cb is not None:
        cb()


bus.signal_subscribe(DEST, "org.freedesktop.portal.Request", "Response",
                     None, None, Gio.DBusSignalFlags.NONE, _on_response)


def call(iface, member, sig, args):
    global token_n
    token_n += 1
    token = f"t{token_n}"
    reply = bus.call_sync(DEST, PATH, iface, member,
                          GLib.Variant(sig, args), None,
                          Gio.DBusCallFlags.NONE, 60000, None)
    req = reply.unpack()[0]
    if req in _responses:
        return _responses.pop(req)
    timed = {"out": False}

    def done():
        loop.quit()

    def bail():
        timed["out"] = True
        loop.quit()
        return False

    _waiters[req] = done
    tid = GLib.timeout_add(90000, bail)
    loop.run()
    if not timed["out"]:
        GLib.source_remove(tid)
    else:
        _waiters.pop(req, None)
        print(f"{member} TIMED OUT on {req}", flush=True)
    return _responses.pop(req, (None, {}))


def main():
    argv = sys.argv[1:]
    do_clip = "--clipboard" in argv
    argv = [a for a in argv if a != "--clipboard"]
    text = argv[0] if argv else "hello"

    st = f"s{token_n}"
    session_path = f"{PATH}/session/{sender}/rd"
    code, res = call(RD, "CreateSession", "(a{sv})",
                     ({"session_handle_token": GLib.Variant("s", "rd"),
                       "handle_token": GLib.Variant("s", "c1")},))
    print("CreateSession", code, dict(res or {}), flush=True)
    session = (res or {}).get("session_handle", session_path)

    code, res = call(RD, "SelectDevices", "(oa{sv})",
                     (session, {"types": GLib.Variant("u", 3),
                                "handle_token": GLib.Variant("s", "c2")}))
    print("SelectDevices", code, flush=True)

    if do_clip:
        bus.call_sync(DEST, PATH, CLIP, "RequestClipboard",
                      GLib.Variant("(oa{sv})", (session, {})),
                      None, Gio.DBusCallFlags.NONE, 5000, None)
        print("RequestClipboard sent", flush=True)

    code, res = call(RD, "Start", "(osa{sv})",
                     (session, "", {"handle_token": GLib.Variant("s", "c3")}))
    print("Start", code, dict(res or {}), flush=True)
    if code != 0:
        return

    # Nudge the pointer with a relative motion (no ScreenCast stream needed),
    # then type the text as evdev keycodes.
    bus.call_sync(DEST, PATH, RD, "NotifyPointerMotion",
                  GLib.Variant("(oa{sv}dd)", (session, {}, 5.0, 5.0)),
                  None, Gio.DBusCallFlags.NONE, 5000, None)
    for ch in text:
        code_ = KEYS.get(ch)
        if code_ is None:
            continue
        for pressed in (1, 0):
            bus.call_sync(DEST, PATH, RD, "NotifyKeyboardKeycode",
                          GLib.Variant("(oa{sv}iu)", (session, {}, code_, pressed)),
                          None, Gio.DBusCallFlags.NONE, 5000, None)
    print("typed", repr(text), flush=True)

    if do_clip:
        bus.call_sync(DEST, PATH, CLIP, "SetSelection",
                      GLib.Variant("(oa{sv})", (session,
                                   {"mime_types": GLib.Variant("as", ["text/plain;charset=utf-8"])})),
                      None, Gio.DBusCallFlags.NONE, 5000, None)
        print("clipboard offered", flush=True)

    GLib.timeout_add(1500, lambda: loop.quit() or False)
    loop.run()


main()
