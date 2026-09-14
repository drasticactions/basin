#!/usr/bin/env python3
"""Bind a GlobalShortcuts preferred trigger and watch it fire.

Speaks org.freedesktop.impl.portal.GlobalShortcuts directly, the way
tests/Basin.Tests/Portal does. It creates a session, binds one shortcut with a
preferred trigger, prints the trigger_description the compositor bound, then
prints the Activated / Deactivated signals it emits when the chord is pressed.
Press the chord through libinput on a DRM run:
    sudo scripts/wlclients/bin/vkbd 29 42 19     # Ctrl+Shift+R

Run with no distro xdg-desktop-portal present so the first caller is adopted as
the frontend and the sender check accepts it.

Usage: portal-gs.py [TRIGGER] [SECONDS]   (default CTRL+SHIFT+r, 25s)
"""
import sys
import gi
gi.require_version("Gio", "2.0")
from gi.repository import Gio, GLib

IMPL = "org.freedesktop.impl.portal.GlobalShortcuts"
DEST = "org.freedesktop.impl.portal.desktop.basin"
ROOT = "/org/freedesktop/portal/desktop"

trigger = sys.argv[1] if len(sys.argv) > 1 else "CTRL+SHIFT+r"
secs = int(sys.argv[2]) if len(sys.argv) > 2 else 25

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
sender = bus.get_unique_name().lstrip(":").replace(".", "_")
loop = GLib.MainLoop()
session = f"{ROOT}/session/{sender}/gs"
request = f"{ROOT}/request/{sender}/r"


def call(member, sig, args):
    return bus.call_sync(DEST, ROOT, IMPL, member, GLib.Variant(sig, args),
                         None, Gio.DBusCallFlags.NONE, 15000, None).unpack()


print("CreateSession", call("CreateSession", "(oosa{sv})",
      (request, session, "org.example.shortcuts", {})), flush=True)

shortcut = ("record", {"description": GLib.Variant("s", "Start recording"),
                       "preferred_trigger": GLib.Variant("s", trigger)})
resp, res = call("BindShortcuts", "(ooa(sa{sv})sa{sv})",
                 (request, session, [shortcut], "", {}))
print("BindShortcuts", resp, flush=True)
for sid, props in res.get("shortcuts", []):
    print("  bound", sid, "->", {k: str(v) for k, v in props.items()}, flush=True)

for name in ("Activated", "Deactivated"):
    bus.signal_subscribe(
        DEST, IMPL, name, ROOT, None, Gio.DBusSignalFlags.NONE,
        (lambda n: lambda c, s, o, i, sn, p: print(f"SIGNAL {n} {p.unpack()}", flush=True))(name))

print(f"bound {trigger}; press it (vkbd 29 42 19). Watching {secs}s.", flush=True)
GLib.timeout_add(secs * 1000, lambda: (loop.quit(), False)[1])
loop.run()
